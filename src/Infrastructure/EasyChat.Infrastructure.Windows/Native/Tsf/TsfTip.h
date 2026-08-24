#pragma once

#include <windows.h>
#include <msctf.h>
#include <InputScope.h>
#include <atomic>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

inline constexpr CLSID CLSID_EasyChatTsf =
{ 0xa1b2c3d4, 0xe5f6, 0x47a8, { 0x91, 0xb2, 0xc3, 0xd4, 0xe5, 0xf6, 0x07, 0x18 } };
inline constexpr GUID GUID_EasyChatTsfProfile =
{ 0xb2c3d4e5, 0xf607, 0x48a9, { 0x92, 0xc3, 0xd4, 0xe5, 0xf6, 0x07, 0x18, 0x29 } };

class EasyChatTsf final : public ITfTextInputProcessorEx,
                          public ITfCompositionSink,
                          public ITfKeyEventSink,
                          public ITfThreadMgrEventSink
{
public:
    EasyChatTsf();
    ~EasyChatTsf();

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** object) override;
    ULONG STDMETHODCALLTYPE AddRef() override;
    ULONG STDMETHODCALLTYPE Release() override;

    HRESULT STDMETHODCALLTYPE Activate(ITfThreadMgr* threadManager, TfClientId clientId) override;
    HRESULT STDMETHODCALLTYPE ActivateEx(ITfThreadMgr* threadManager, TfClientId clientId, DWORD flags) override;
    HRESULT STDMETHODCALLTYPE Deactivate() override;

    HRESULT STDMETHODCALLTYPE OnCompositionTerminated(TfEditCookie cookie,
                                                       ITfComposition* composition) override;

    HRESULT STDMETHODCALLTYPE OnSetFocus(BOOL foreground) override;
    HRESULT STDMETHODCALLTYPE OnTestKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    HRESULT STDMETHODCALLTYPE OnTestKeyUp(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    HRESULT STDMETHODCALLTYPE OnKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    HRESULT STDMETHODCALLTYPE OnKeyUp(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    HRESULT STDMETHODCALLTYPE OnPreservedKey(ITfContext* context, REFGUID key, BOOL* eaten) override;

    HRESULT STDMETHODCALLTYPE OnSetFocus(ITfDocumentMgr* prevDocumentManager, ITfDocumentMgr* nextDocumentManager) override;
    HRESULT STDMETHODCALLTYPE OnInitDocumentMgr(ITfDocumentMgr* documentManager) override;
    HRESULT STDMETHODCALLTYPE OnUninitDocumentMgr(ITfDocumentMgr* documentManager) override;
    HRESULT STDMETHODCALLTYPE OnPushContext(ITfContext* context) override;
    HRESULT STDMETHODCALLTYPE OnPopContext(ITfContext* context) override;

    static LRESULT CALLBACK NotificationWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam);

private:
    struct Command { std::string type; std::string session; long long revision{}; std::string text; };
    friend class EasyChatTsfEditSession;
    enum class EditOperation { Start, Update, Apply, End, Sync };
    HRESULT RequestEdit(ITfContext* context, EditOperation operation, const std::wstring& text = {}, const Command* command = nullptr);
    HRESULT ExecuteEdit(TfEditCookie cookie, ITfContext* context, EditOperation operation, const std::wstring& text, const Command* command);
    HRESULT ReplaceComposition(TfEditCookie cookie, const std::wstring& text);
    HRESULT EndComposition(TfEditCookie cookie);
    void StartPipe();
    void StopPipe();
    void PipeLoop();
    void SendUpdated(const std::string& text, bool final);
    void SendMessage(const std::string& type, const std::string& session, long long revision,
                     const std::string& text, bool final, bool isPassword = false);
    void DrainCommands(ITfContext* context);
    static std::string EscapeJson(const std::string& text);
    static std::string CurrentPipeName();
    std::string CaretJson() const;
    void DrainCommandsOnThread();
    void UpdateCaret(TfEditCookie cookie, ITfContext* context, ITfRange* range);
    bool IsProtectedContext(TfEditCookie cookie, ITfContext* context, ITfRange* range) const;
    void CloseNotificationWindow();

    std::atomic_ulong _refCount{ 1 };
    ITfThreadMgr* _threadManager{};
    ITfContext* _context{};
    ITfComposition* _compositionObject{};
    HWND _notificationWindow{};
    DWORD _threadId{};
    TfClientId _clientId{};
    DWORD _threadSinkCookie{};
    DWORD _keySinkCookie{};
    std::thread _pipeThread;
    std::atomic_bool _stopPipe{ false };
    HANDLE _pipe{ INVALID_HANDLE_VALUE };
    std::mutex _pipeMutex;
    std::mutex _commandMutex;
    std::vector<Command> _commands;
    std::string _session;
    long long _revision{};
    std::string _composition;
    bool _endAccepted{};
    bool _isPassword{};
    bool _ignoreCompositionUpdate{};
    RECT _caret{};
    bool _hasCaret{};
};

class EasyChatClassFactory final : public IClassFactory
{
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** object) override;
    ULONG STDMETHODCALLTYPE AddRef() override;
    ULONG STDMETHODCALLTYPE Release() override;
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** object) override;
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override;
private:
    std::atomic_ulong _refCount{ 1 };
};

extern HINSTANCE g_module;
extern std::atomic_ulong g_serverLocks;
extern std::atomic_ulong g_objectCount;
