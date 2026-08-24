#include "TsfTip.h"
#include <oleauto.h>
#include <sddl.h>
#include <shlwapi.h>
#include <strsafe.h>
#include <algorithm>
#include <cstdio>

HINSTANCE g_module{};
std::atomic_ulong g_serverLocks{ 0 };
std::atomic_ulong g_objectCount{ 0 };

// InputScope.h declares this property GUID but the Windows SDK does not export
// it from a library, so the TIP provides the single definition required by the linker.
extern "C" const GUID GUID_PROP_INPUTSCOPE =
{ 0x1713dd5a, 0x68e7, 0x4a5b, { 0x9a, 0xf6, 0x59, 0x2a, 0x59, 0x5c, 0x77, 0x8d } };

namespace {
constexpr char kProtocol[] = "\"protocolVersion\":1";
constexpr UINT kDrainCommandsMessage = WM_APP + 0x2A;
constexpr wchar_t kNotificationWindowClass[] = L"EasyChat.EasyChatTsf.Notification";
constexpr LONG kPasswordInputScope = 31;

std::once_flag g_notificationClassRegistration;

void RegisterNotificationClass() {
    std::call_once(g_notificationClassRegistration, [] {
        WNDCLASSEXW windowClass{};
        windowClass.cbSize = sizeof(windowClass);
        windowClass.lpfnWndProc = EasyChatTsf::NotificationWindowProc;
        windowClass.hInstance = g_module;
        windowClass.lpszClassName = kNotificationWindowClass;
        RegisterClassExW(&windowClass);
    });
}

std::string ToUtf8(const std::wstring& value) {
    if (value.empty()) return {};
    int size = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    std::string result(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), size, nullptr, nullptr);
    return result;
}

bool IsTsfProfileRegistered() {
    wchar_t clsidText[64]{};
    if (FAILED(StringFromGUID2(CLSID_EasyChatTsf, clsidText, 64)))
        return false;

    const std::wstring keyPath =
        L"Software\\Microsoft\\CTF\\TIP\\" + std::wstring(clsidText);
    HKEY key{};
    const auto result = RegOpenKeyExW(HKEY_LOCAL_MACHINE, keyPath.c_str(), 0, KEY_READ, &key);
    if (result != ERROR_SUCCESS)
        return false;
    RegCloseKey(key);
    return true;
}

std::wstring ToWide(const std::string& value) {
    if (value.empty()) return {};
    int size = MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0);
    std::wstring result(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), size);
    return result;
}

std::string ReadJsonString(const std::string& json, const char* name) {
    const auto key = std::string("\"") + name + "\":\"";
    const auto begin = json.find(key);
    if (begin == std::string::npos) return {};
    auto value = begin + key.size();
    std::string result;
    for (; value < json.size() && json[value] != '"'; ++value) {
        if (json[value] == '\\' && value + 1 < json.size()) ++value;
        result.push_back(json[value]);
    }
    return result;
}

long long ReadJsonNumber(const std::string& json, const char* name) {
    const auto key = std::string("\"") + name + "\":";
    const auto begin = json.find(key);
    if (begin == std::string::npos) return 0;
    return _strtoi64(json.c_str() + begin + key.size(), nullptr, 10);
}

bool ReadJsonBool(const std::string& json, const char* name) {
    const auto key = std::string("\"") + name + "\":true";
    return json.find(key) != std::string::npos;
}
}

LRESULT CALLBACK EasyChatTsf::NotificationWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    auto* owner = reinterpret_cast<EasyChatTsf*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        const auto* create = reinterpret_cast<const CREATESTRUCTW*>(lParam);
        owner = static_cast<EasyChatTsf*>(create->lpCreateParams);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(owner));
        return TRUE;
    }
    if (owner && message == kDrainCommandsMessage) {
        owner->DrainCommandsOnThread();
        return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

class EasyChatTsfEditSession final : public ITfEditSession {
public:
    EasyChatTsfEditSession(EasyChatTsf* owner, ITfContext* context,
                           EasyChatTsf::EditOperation operation, std::wstring text,
                           EasyChatTsf::Command command)
        : _owner(owner), _context(context), _operation(operation),
          _text(std::move(text)), _command(std::move(command)) {
        if (_context) _context->AddRef();
    }

    ~EasyChatTsfEditSession() {
        if (_context) _context->Release();
    }

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** object) override {
        if (!object) return E_POINTER;
        *object = nullptr;
        if (riid == IID_IUnknown || riid == __uuidof(ITfEditSession))
            *object = static_cast<ITfEditSession*>(this);
        else return E_NOINTERFACE;
        AddRef();
        return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++_refCount; }
    ULONG STDMETHODCALLTYPE Release() override {
        const auto value = --_refCount;
        if (!value) delete this;
        return value;
    }
    HRESULT STDMETHODCALLTYPE DoEditSession(TfEditCookie cookie) override {
        return _owner->ExecuteEdit(cookie, _context, _operation, _text, &_command);
    }

private:
    std::atomic_ulong _refCount{ 1 };
    EasyChatTsf* _owner;
    ITfContext* _context;
    EasyChatTsf::EditOperation _operation;
    std::wstring _text;
    EasyChatTsf::Command _command;
};

EasyChatTsf::EasyChatTsf() { ++g_objectCount; }

EasyChatTsf::~EasyChatTsf() {
    Deactivate();
    if (_compositionObject) { _compositionObject->Release(); _compositionObject = nullptr; }
    if (_context) { _context->Release(); _context = nullptr; }
    --g_objectCount;
}

HRESULT STDMETHODCALLTYPE EasyChatTsf::QueryInterface(REFIID riid, void** object) {
    if (!object) return E_POINTER;
    *object = nullptr;
    if (riid == IID_IUnknown || riid == __uuidof(ITfTextInputProcessor) || riid == __uuidof(ITfTextInputProcessorEx))
        *object = static_cast<ITfTextInputProcessorEx*>(this);
    else if (riid == __uuidof(ITfCompositionSink)) *object = static_cast<ITfCompositionSink*>(this);
    else if (riid == __uuidof(ITfKeyEventSink)) *object = static_cast<ITfKeyEventSink*>(this);
    else if (riid == __uuidof(ITfThreadMgrEventSink)) *object = static_cast<ITfThreadMgrEventSink*>(this);
    else return E_NOINTERFACE;
    AddRef();
    return S_OK;
}

ULONG STDMETHODCALLTYPE EasyChatTsf::AddRef() { return ++_refCount; }
ULONG STDMETHODCALLTYPE EasyChatTsf::Release() { auto value = --_refCount; if (!value) delete this; return value; }

HRESULT STDMETHODCALLTYPE EasyChatTsf::Activate(ITfThreadMgr* threadManager, TfClientId clientId) {
    return ActivateEx(threadManager, clientId, 0);
}

HRESULT STDMETHODCALLTYPE EasyChatTsf::ActivateEx(ITfThreadMgr* threadManager, TfClientId clientId, DWORD) {
    if (!threadManager) return E_INVALIDARG;
    _threadId = GetCurrentThreadId();
    RegisterNotificationClass();
    _notificationWindow = CreateWindowExW(
        0,
        kNotificationWindowClass,
        L"EasyChat TSF",
        0,
        0,
        0,
        0,
        0,
        HWND_MESSAGE,
        nullptr,
        g_module,
        this);
    _threadManager = threadManager;
    _threadManager->AddRef();
    _clientId = clientId;
    if (auto* source = static_cast<ITfSource*>(nullptr); SUCCEEDED(_threadManager->QueryInterface(IID_PPV_ARGS(&source)))) {
        source->AdviseSink(__uuidof(ITfThreadMgrEventSink), static_cast<ITfThreadMgrEventSink*>(this), &_threadSinkCookie);
        source->Release();
    }
    if (auto* keystrokes = static_cast<ITfKeystrokeMgr*>(nullptr); SUCCEEDED(_threadManager->QueryInterface(IID_PPV_ARGS(&keystrokes)))) {
        keystrokes->AdviseKeyEventSink(_clientId, static_cast<ITfKeyEventSink*>(this), TRUE);
        keystrokes->Release();
    }
    StartPipe();
    return S_OK;
}

HRESULT STDMETHODCALLTYPE EasyChatTsf::Deactivate() {
    StopPipe();
    CloseNotificationWindow();
    if (_threadManager) {
        if (auto* source = static_cast<ITfSource*>(nullptr); SUCCEEDED(_threadManager->QueryInterface(IID_PPV_ARGS(&source)))) {
            if (_threadSinkCookie) source->UnadviseSink(_threadSinkCookie);
            source->Release();
        }
        if (auto* keystrokes = static_cast<ITfKeystrokeMgr*>(nullptr); SUCCEEDED(_threadManager->QueryInterface(IID_PPV_ARGS(&keystrokes)))) {
            keystrokes->UnadviseKeyEventSink(_clientId);
            keystrokes->Release();
        }
    }
    if (_threadManager) { _threadManager->Release(); _threadManager = nullptr; }
    return S_OK;
}

HRESULT STDMETHODCALLTYPE EasyChatTsf::OnCompositionTerminated(TfEditCookie /*cookie*/, ITfComposition*) {
    if (!_session.empty()) SendMessage(_endAccepted ? "composition.accepted" : "composition.cancelled", _session, _revision, {}, _endAccepted);
    _composition.clear();
    _endAccepted = false;
    _isPassword = false;
    _hasCaret = false;
    if (_compositionObject) { _compositionObject->Release(); _compositionObject = nullptr; }
    if (_context) { _context->Release(); _context = nullptr; }
    _session.clear();
    return S_OK;
}
HRESULT STDMETHODCALLTYPE EasyChatTsf::OnSetFocus(BOOL foreground) {
    if (!foreground && _context && !_composition.empty())
        RequestEdit(_context, EditOperation::End);
    return S_OK;
}
HRESULT STDMETHODCALLTYPE EasyChatTsf::OnSetFocus(ITfDocumentMgr*, ITfDocumentMgr*) { return S_OK; }
HRESULT STDMETHODCALLTYPE EasyChatTsf::OnInitDocumentMgr(ITfDocumentMgr*) { return S_OK; }
HRESULT STDMETHODCALLTYPE EasyChatTsf::OnUninitDocumentMgr(ITfDocumentMgr*) { return S_OK; }
HRESULT STDMETHODCALLTYPE EasyChatTsf::OnPushContext(ITfContext*) { return S_OK; }
HRESULT STDMETHODCALLTYPE EasyChatTsf::OnPopContext(ITfContext*) { return S_OK; }

HRESULT STDMETHODCALLTYPE EasyChatTsf::OnTestKeyDown(ITfContext*, WPARAM wParam, LPARAM, BOOL* eaten) {
    if (!eaten) return E_POINTER;
    if (_isPassword) { *eaten = FALSE; return S_OK; }
    *eaten = !_composition.empty()
        && (wParam == VK_RETURN || wParam == VK_ESCAPE || wParam == VK_TAB || wParam == VK_BACK);
    return S_OK;
}
HRESULT STDMETHODCALLTYPE EasyChatTsf::OnTestKeyUp(ITfContext*, WPARAM, LPARAM, BOOL* eaten) { if (!eaten) return E_POINTER; *eaten = FALSE; return S_OK; }
HRESULT STDMETHODCALLTYPE EasyChatTsf::OnKeyUp(ITfContext*, WPARAM, LPARAM, BOOL* eaten) { if (!eaten) return E_POINTER; *eaten = FALSE; return S_OK; }
HRESULT STDMETHODCALLTYPE EasyChatTsf::OnPreservedKey(ITfContext*, REFGUID, BOOL* eaten) { if (!eaten) return E_POINTER; *eaten = FALSE; return S_OK; }

HRESULT STDMETHODCALLTYPE EasyChatTsf::OnKeyDown(ITfContext* context, WPARAM wParam, LPARAM, BOOL* eaten) {
    if (!eaten) return E_POINTER;
    *eaten = FALSE;
    if (_isPassword)
        return S_OK;
    if (context && context != _context) {
        if (_context) _context->Release();
        _context = context;
        _context->AddRef();
    }
    DrainCommands(context);
    if (wParam == VK_ESCAPE) {
        if (!_composition.empty()) {
            RequestEdit(context, EditOperation::End);
            *eaten = TRUE;
        }
        return S_OK;
    }
    if (wParam == VK_RETURN || wParam == VK_TAB) {
        if (!_composition.empty()) {
            _endAccepted = true;
            RequestEdit(context, EditOperation::End);
            *eaten = FALSE;
        }
        return S_OK;
    }
    if (wParam == VK_BACK) {
        if (_composition.empty())
            return S_OK;
        auto wide = ToWide(_composition);
        if (!wide.empty()) wide.pop_back();
        _composition = ToUtf8(wide);
        if (_composition.empty()) RequestEdit(context, EditOperation::End);
        else RequestEdit(context, EditOperation::Update, wide);
        ++_revision;
        if (!_composition.empty()) SendUpdated(_composition, false);
        *eaten = TRUE;
        return S_OK;
    }
    BYTE keyboard[256]{};
    GetKeyboardState(keyboard);
    WCHAR chars[4]{};
    const auto count = ToUnicode(static_cast<UINT>(wParam), MapVirtualKeyW(static_cast<UINT>(wParam), MAPVK_VK_TO_VSC), keyboard, chars, 4, 0);
    if (count > 0) {
        auto wide = ToWide(_composition);
        wide.append(chars, chars + count);
        if (_session.empty()) {
            _session = "native-" + std::to_string(GetTickCount64());
            _revision = 0;
        }
        const auto operation = _compositionObject ? EditOperation::Update : EditOperation::Start;
        const auto editResult = RequestEdit(context, operation, wide);
        if (_isPassword) {
            _composition.clear();
            *eaten = FALSE;
            return S_OK;
        }
        if (FAILED(editResult))
            return S_OK;
        _composition = ToUtf8(wide);
        ++_revision;
        SendUpdated(_composition, false);
        *eaten = TRUE;
    }
    return S_OK;
}

void EasyChatTsf::StartPipe() {
    _stopPipe = false;
    _pipeThread = std::thread([this] { PipeLoop(); });
}
void EasyChatTsf::StopPipe() {
    _stopPipe = true;
    HANDLE pipe = INVALID_HANDLE_VALUE;
    {
        std::lock_guard lock(_pipeMutex);
        pipe = _pipe;
        _pipe = INVALID_HANDLE_VALUE;
    }
    if (pipe != INVALID_HANDLE_VALUE) {
        CancelIoEx(pipe, nullptr);
        CloseHandle(pipe);
    }
    if (_pipeThread.joinable()) _pipeThread.join();
}

void EasyChatTsf::PipeLoop() {
    while (!_stopPipe) {
        const auto pipeName = ToWide(CurrentPipeName());
        HANDLE pipe = CreateFileW(pipeName.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (pipe == INVALID_HANDLE_VALUE) { Sleep(250); continue; }
        { std::lock_guard lock(_pipeMutex); _pipe = pipe; }
        SendMessage("hello", {}, 0, {}, false);
        std::string buffer;
        bool protocolInvalid = false;
        char chunk[1024]; DWORD read = 0;
        while (!_stopPipe && !protocolInvalid && ReadFile(pipe, chunk, sizeof(chunk), &read, nullptr) && read > 0) {
            buffer.append(chunk, chunk + read);
            size_t end;
            while ((end = buffer.find('\n')) != std::string::npos) {
                auto line = buffer.substr(0, end); buffer.erase(0, end + 1);
                if (ReadJsonNumber(line, "protocolVersion") != 1) {
                    SendMessage("error", {}, 0, "protocol-version-mismatch", false);
                    buffer.clear();
                    protocolInvalid = true;
                    break;
                }
                auto type = ReadJsonString(line, "type");
                if (type.rfind("translation.", 0) == 0) {
                    std::lock_guard<std::mutex> lock(_commandMutex);
                    _commands.emplace_back(Command{ type, ReadJsonString(line, "session"), ReadJsonNumber(line, "revision"), ReadJsonString(line, "text") });
                    if (_notificationWindow)
                        PostMessageW(_notificationWindow, kDrainCommandsMessage, 0, 0);
                }
            }
        }
        bool closePipe = false;
        {
            std::lock_guard lock(_pipeMutex);
            if (_pipe == pipe) {
                _pipe = INVALID_HANDLE_VALUE;
                closePipe = true;
            }
        }
        if (closePipe) CloseHandle(pipe);
    }
}

void EasyChatTsf::DrainCommands(ITfContext*) {
    std::vector<Command> commands;
    {
        std::lock_guard<std::mutex> lock(_commandMutex);
        commands.swap(_commands);
    }
    for (const auto& command : commands) {
        if (command.session != _session || command.revision != _revision) continue;
        if (command.type == "translation.cancel") RequestEdit(_context, EditOperation::End);
        else if (command.type == "translation.preview") RequestEdit(_context, EditOperation::Apply, ToWide(command.text), &command);
        else if (command.type == "translation.commit") RequestEdit(_context, EditOperation::Apply, ToWide(command.text), &command);
    }
}

void EasyChatTsf::DrainCommandsOnThread() {
    if (_context)
        DrainCommands(_context);
}

HRESULT EasyChatTsf::RequestEdit(ITfContext* context, EditOperation operation, const std::wstring& text, const Command* command) {
    if (!context) return E_UNEXPECTED;
    EasyChatTsf::Command value = command ? *command : Command{};
    auto* session = new (std::nothrow) EasyChatTsfEditSession(this, context, operation, text, std::move(value));
    if (!session) return E_OUTOFMEMORY;
    HRESULT sessionResult = E_FAIL;
    const auto result = context->RequestEditSession(_clientId, session, TF_ES_SYNC | TF_ES_READWRITE, &sessionResult);
    session->Release();
    return FAILED(result) ? result : sessionResult;
}

HRESULT EasyChatTsf::ExecuteEdit(TfEditCookie cookie, ITfContext* context, EditOperation operation, const std::wstring& text, const Command* command) {
    if (operation == EditOperation::Start) {
        TF_SELECTION selection{}; ULONG fetched = 0;
        auto result = context->GetSelection(cookie, TF_DEFAULT_SELECTION, 1, &selection, &fetched);
        if (FAILED(result) || fetched == 0 || !selection.range) return FAILED(result) ? result : E_FAIL;
        _isPassword = IsProtectedContext(cookie, context, selection.range);
        UpdateCaret(cookie, context, selection.range);
        if (_isPassword) {
            if (_session.empty()) _session = "native-" + std::to_string(GetTickCount64());
            SendMessage("composition.updated", _session, _revision, {}, false, true);
            selection.range->Release();
            return S_OK;
        }
        ITfContextComposition* compositionServices = nullptr;
        result = context->QueryInterface(IID_PPV_ARGS(&compositionServices));
        if (SUCCEEDED(result)) {
            result = compositionServices->StartComposition(cookie, selection.range, this, &_compositionObject);
            compositionServices->Release();
        }
        selection.range->Release();
        if (FAILED(result)) return result;
        return ReplaceComposition(cookie, text);
    }
    if (operation == EditOperation::Update)
        return ReplaceComposition(cookie, text);
    if (operation == EditOperation::Apply) {
        if (command && command->type == "translation.commit") {
            _endAccepted = true;
            auto result = ReplaceComposition(cookie, text);
            if (SUCCEEDED(result)) result = EndComposition(cookie);
            return result;
        }
        if (command && command->type == "translation.cancel")
            return EndComposition(cookie);
        return ReplaceComposition(cookie, text);
    }
    if (operation == EditOperation::Sync) {
        if (!_compositionObject) return S_OK;
        ITfRange* range = nullptr;
        auto result = _compositionObject->GetRange(&range);
        if (FAILED(result) || !range) return FAILED(result) ? result : E_FAIL;
        std::vector<wchar_t> buffer(4096);
        ULONG count = 0;
        result = range->GetText(cookie, 0, buffer.data(), static_cast<ULONG>(buffer.size() - 1), &count);
        if (SUCCEEDED(result)) {
            buffer[count] = L'\0';
            const auto source = ToUtf8(std::wstring(buffer.data(), count));
            if (source != _composition) {
                _composition = source;
                ++_revision;
                UpdateCaret(cookie, context, range);
                SendUpdated(_composition, false);
            } else {
                UpdateCaret(cookie, context, range);
            }
        }
        range->Release();
        return result;
    }
    return EndComposition(cookie);
}

HRESULT EasyChatTsf::ReplaceComposition(TfEditCookie cookie, const std::wstring& text) {
    if (!_compositionObject) return E_UNEXPECTED;
    ITfRange* range = nullptr;
    auto result = _compositionObject->GetRange(&range);
    if (SUCCEEDED(result)) {
        _ignoreCompositionUpdate = true;
        result = range->SetText(cookie, 0, text.c_str(), static_cast<LONG>(text.size()));
        _ignoreCompositionUpdate = false;
    }
    if (SUCCEEDED(result)) UpdateCaret(cookie, _context, range);
    if (range) range->Release();
    return result;
}

HRESULT EasyChatTsf::EndComposition(TfEditCookie cookie) {
    if (!_compositionObject) return S_OK;
    const auto result = _compositionObject->EndComposition(cookie);
    _composition.clear();
    return result;
}

void EasyChatTsf::SendUpdated(const std::string& text, bool final) { SendMessage("composition.updated", _session, _revision, text, final); }
void EasyChatTsf::SendMessage(const std::string& type, const std::string& session, long long revision, const std::string& text, bool final, bool isPassword) {
    std::lock_guard lock(_pipeMutex);
    if (_pipe == INVALID_HANDLE_VALUE) return;
    const auto payload = std::string("{\"") + "type\":\"" + type + "\",\"session\":\"" + EscapeJson(session) + "\",\"revision\":" + std::to_string(revision) + ",\"text\":\"" + EscapeJson(text) + "\",\"isFinal\":" + (final ? "true" : "false") + "," + CaretJson() + ",\"isPassword\":" + (isPassword ? "true" : "false") + "," + kProtocol + "}\n";
    DWORD written = 0; WriteFile(_pipe, payload.data(), static_cast<DWORD>(payload.size()), &written, nullptr);
}
std::string EasyChatTsf::EscapeJson(const std::string& text) { std::string result; for (char c : text) { if (c == '\\' || c == '"') result.push_back('\\'); if (c == '\n') { result += "\\n"; continue; } result.push_back(c); } return result; }
std::string EasyChatTsf::CurrentPipeName() { HANDLE token{}; if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return "\\\\.\\pipe\\EasyChat.Tsf.unknown"; DWORD size{}; GetTokenInformation(token, TokenUser, nullptr, 0, &size); std::vector<BYTE> data(size); GetTokenInformation(token, TokenUser, data.data(), size, &size); CloseHandle(token); LPWSTR sid{}; ConvertSidToStringSidW(reinterpret_cast<PTOKEN_USER>(data.data())->User.Sid, &sid); std::wstring sidValue = sid ? sid : L"unknown"; LocalFree(sid); return "\\\\.\\pipe\\EasyChat.Tsf." + ToUtf8(sidValue); }
void EasyChatTsf::UpdateCaret(TfEditCookie cookie, ITfContext* context, ITfRange* range) {
    _hasCaret = false;
    _caret = {};
    if (!context || !range)
        return;

    ITfContextView* view = nullptr;
    if (FAILED(context->GetActiveView(&view)) || !view)
        return;

    RECT caret{};
    if (SUCCEEDED(view->GetScreenExt(&caret))) {
        _caret = caret;
        _hasCaret = caret.right >= caret.left && caret.bottom >= caret.top;
    }
    view->Release();
}

bool EasyChatTsf::IsProtectedContext(TfEditCookie cookie, ITfContext* context, ITfRange* range) const {
    if (!context || !range)
        return false;

    ITfProperty* property = nullptr;
    if (FAILED(context->GetProperty(GUID_PROP_INPUTSCOPE, &property)) || !property)
        return false;

    ITfReadOnlyProperty* readOnlyProperty = nullptr;
    if (FAILED(property->QueryInterface(IID_PPV_ARGS(&readOnlyProperty))) || !readOnlyProperty) {
        property->Release();
        return false;
    }

    VARIANT value{};
    VariantInit(&value);
    bool protectedInput = false;
    if (SUCCEEDED(readOnlyProperty->GetValue(cookie, range, &value))) {
        switch (value.vt) {
        case VT_I1: protectedInput = value.cVal == kPasswordInputScope; break;
        case VT_UI1: protectedInput = value.bVal == kPasswordInputScope; break;
        case VT_I2: protectedInput = value.iVal == kPasswordInputScope; break;
        case VT_UI2: protectedInput = value.uiVal == kPasswordInputScope; break;
        case VT_I4: protectedInput = value.lVal == kPasswordInputScope; break;
        case VT_UI4: protectedInput = value.ulVal == kPasswordInputScope; break;
        case VT_UNKNOWN: {
            ITfInputScope* inputScope = nullptr;
            if (value.punkVal && SUCCEEDED(value.punkVal->QueryInterface(IID_PPV_ARGS(&inputScope)))) {
                UINT count = 0;
                InputScope* scopes = nullptr;
                if (SUCCEEDED(inputScope->GetInputScopes(&scopes, &count)) && scopes) {
                    for (ULONG index = 0; index < count; ++index) {
                        if (scopes[index] == IS_PASSWORD || scopes[index] == IS_NUMERIC_PASSWORD
                            || scopes[index] == IS_NUMERIC_PIN || scopes[index] == IS_ALPHANUMERIC_PIN
                            || scopes[index] == IS_ALPHANUMERIC_PIN_SET) {
                            protectedInput = true;
                            break;
                        }
                    }
                    CoTaskMemFree(scopes);
                }
                inputScope->Release();
            }
            break;
        }
        default:
            break;
        }
    }
    VariantClear(&value);
    readOnlyProperty->Release();
    property->Release();
    return protectedInput;
}

void EasyChatTsf::CloseNotificationWindow() {
    const auto window = _notificationWindow;
    _notificationWindow = nullptr;
    if (window)
        DestroyWindow(window);
}

std::string EasyChatTsf::CaretJson() const {
    if (!_hasCaret)
        return "\"caretX\":0,\"caretY\":0,\"caretWidth\":0,\"caretHeight\":0";
    return std::string("\"caretX\":") + std::to_string(_caret.left)
        + ",\"caretY\":" + std::to_string(_caret.top)
        + ",\"caretWidth\":" + std::to_string(_caret.right - _caret.left)
        + ",\"caretHeight\":" + std::to_string(_caret.bottom - _caret.top);
}

HRESULT STDMETHODCALLTYPE EasyChatClassFactory::QueryInterface(REFIID riid, void** object) { if (!object) return E_POINTER; *object = nullptr; if (riid == IID_IUnknown || riid == IID_IClassFactory) *object = static_cast<IClassFactory*>(this); else return E_NOINTERFACE; AddRef(); return S_OK; }
ULONG STDMETHODCALLTYPE EasyChatClassFactory::AddRef() { return ++_refCount; }
ULONG STDMETHODCALLTYPE EasyChatClassFactory::Release() { auto value = --_refCount; if (!value) delete this; return value; }
HRESULT STDMETHODCALLTYPE EasyChatClassFactory::CreateInstance(IUnknown* outer, REFIID riid, void** object) { if (outer) return CLASS_E_NOAGGREGATION; auto* tip = new (std::nothrow) EasyChatTsf(); if (!tip) return E_OUTOFMEMORY; const auto result = tip->QueryInterface(riid, object); tip->Release(); return result; }
HRESULT STDMETHODCALLTYPE EasyChatClassFactory::LockServer(BOOL lock) { if (lock) ++g_serverLocks; else --g_serverLocks; return S_OK; }

STDAPI DllCanUnloadNow() { return g_serverLocks == 0 && g_objectCount == 0 ? S_OK : S_FALSE; }
STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** object) { if (clsid != CLSID_EasyChatTsf) return CLASS_E_CLASSNOTAVAILABLE; auto* factory = new (std::nothrow) EasyChatClassFactory(); if (!factory) return E_OUTOFMEMORY; const auto result = factory->QueryInterface(riid, object); factory->Release(); return result; }

extern "C" __declspec(dllexport) HRESULT __stdcall DllRegisterServer() {
    wchar_t path[MAX_PATH]{}; GetModuleFileNameW(g_module, path, MAX_PATH);
    HKEY clsid{}; wchar_t clsidText[64]{}; StringFromGUID2(CLSID_EasyChatTsf, clsidText, 64);
    std::wstring key = L"Software\\Classes\\CLSID\\" + std::wstring(clsidText) + L"\\InprocServer32";
    if (RegCreateKeyExW(HKEY_CURRENT_USER, key.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &clsid, nullptr) != ERROR_SUCCESS) return E_ACCESSDENIED;
    RegSetValueExW(clsid, nullptr, 0, REG_SZ, reinterpret_cast<const BYTE*>(path), static_cast<DWORD>((wcslen(path) + 1) * sizeof(wchar_t)));
    const wchar_t model[] = L"Apartment"; RegSetValueExW(clsid, L"ThreadingModel", 0, REG_SZ, reinterpret_cast<const BYTE*>(model), sizeof(model)); RegCloseKey(clsid);
    const auto init = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(init) && init != RPC_E_CHANGED_MODE) return init;

    HRESULT result = S_OK;
    ITfInputProcessorProfiles* profiles = nullptr;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
                                   IID_PPV_ARGS(&profiles)))) {
        // ITfInputProcessorProfiles::Register writes the machine-wide TSF catalog and
        // requires elevation. Once the profile exists (for example, after MSI setup),
        // startup only needs the per-user COM registration above and must not retry it.
        const bool profileRegistered = IsTsfProfileRegistered();
        if (!profileRegistered)
            result = profiles->Register(CLSID_EasyChatTsf);

        if (SUCCEEDED(result) && !profileRegistered) {
            constexpr LANGID languages[] = {
                MAKELANGID(LANG_ENGLISH, SUBLANG_ENGLISH_US),
                MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED)
            };
            for (const auto language : languages) {
                profiles->RemoveLanguageProfile(CLSID_EasyChatTsf, language, GUID_EasyChatTsfProfile);
                result = profiles->AddLanguageProfile(
                    CLSID_EasyChatTsf, language, GUID_EasyChatTsfProfile,
                    L"EasyChat Translate", 16, path, static_cast<ULONG>(wcslen(path)), 0);
                if (FAILED(result)) break;
            }
        }
        profiles->Release();
    }
    if (init != RPC_E_CHANGED_MODE) CoUninitialize();
    return result;
}
extern "C" __declspec(dllexport) HRESULT __stdcall DllUnregisterServer() {
    const auto init = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(init) && init != RPC_E_CHANGED_MODE) return init;
    ITfInputProcessorProfiles* profiles = nullptr;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
                                   IID_PPV_ARGS(&profiles)))) {
        profiles->Unregister(CLSID_EasyChatTsf);
        profiles->Release();
    }
    if (init != RPC_E_CHANGED_MODE) CoUninitialize();
    wchar_t clsidText[64]{}; StringFromGUID2(CLSID_EasyChatTsf, clsidText, 64);
    std::wstring key = L"Software\\Classes\\CLSID\\" + std::wstring(clsidText);
    const auto deleted = SHDeleteKeyW(HKEY_CURRENT_USER, key.c_str());
    return deleted == ERROR_SUCCESS || deleted == ERROR_FILE_NOT_FOUND || deleted == ERROR_PATH_NOT_FOUND
        ? S_OK
        : HRESULT_FROM_WIN32(deleted);
}

extern "C" __declspec(dllexport) HRESULT __stdcall DllActivateProfile() {
    const auto init = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(init) && init != RPC_E_CHANGED_MODE)
        return init;

    HRESULT result = E_NOINTERFACE;
    ITfInputProcessorProfiles* profiles = nullptr;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
                                   IID_PPV_ARGS(&profiles)))) {
        LANGID language = LOWORD(reinterpret_cast<ULONG_PTR>(GetKeyboardLayout(0)));
        if (language != MAKELANGID(LANG_ENGLISH, SUBLANG_ENGLISH_US)
            && language != MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED)) {
            language = MAKELANGID(LANG_ENGLISH, SUBLANG_ENGLISH_US);
        }
        result = profiles->ActivateLanguageProfile(CLSID_EasyChatTsf, language, GUID_EasyChatTsfProfile);
        profiles->Release();
    }

    if (init != RPC_E_CHANGED_MODE)
        CoUninitialize();
    return result;
}

BOOL APIENTRY DllMain(HINSTANCE module, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) { g_module = module; DisableThreadLibraryCalls(module); } return TRUE; }
