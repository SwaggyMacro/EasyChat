#include "EasyChat.ImeHook.Native.h"

#include <imm.h>
#include <string>
#include <vector>

#pragma comment(lib, "Imm32.lib")

namespace
{
    HINSTANCE g_instance = nullptr;
    HHOOK g_hook = nullptr;
    volatile LONG g_lastError = ERROR_SUCCESS;

    INIT_ONCE g_logInit = INIT_ONCE_STATIC_INIT;
    HANDLE g_logMutex = nullptr;

    BOOL CALLBACK InitializeLogMutex(PINIT_ONCE, PVOID, PVOID*)
    {
        g_logMutex = CreateMutexW(nullptr, FALSE, L"Local\\EasyChat.ImeCompositionHook.Log");
        return TRUE;
    }

    void SetLastErrorValue(DWORD error)
    {
        InterlockedExchange(&g_lastError, static_cast<LONG>(error));
    }

    std::wstring Escape(const std::wstring& value)
    {
        std::wstring escaped;
        escaped.reserve(value.size());
        for (const wchar_t character : value)
        {
            switch (character)
            {
            case L'\\': escaped.append(L"\\\\"); break;
            case L'"': escaped.append(L"\\\""); break;
            case L'\r': escaped.append(L"\\r"); break;
            case L'\n': escaped.append(L"\\n"); break;
            case L'\t': escaped.append(L"\\t"); break;
            default: escaped.push_back(character); break;
            }
        }
        return escaped;
    }

    std::string ToUtf8(const std::wstring& value)
    {
        if (value.empty())
            return {};

        const int byteCount = WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            nullptr,
            0,
            nullptr,
            nullptr);
        if (byteCount <= 0)
            return {};

        std::string result(static_cast<size_t>(byteCount), '\0');
        WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            byteCount,
            nullptr,
            nullptr);
        return result;
    }

    void WriteLog(const std::wstring& line)
    {
        const std::string utf8 = ToUtf8(line + L"\r\n");
        if (utf8.empty())
            return;

        wchar_t tempPath[MAX_PATH]{};
        const DWORD pathLength = GetTempPathW(MAX_PATH, tempPath);
        if (pathLength == 0 || pathLength >= MAX_PATH)
            return;

        std::wstring path(tempPath, pathLength);
        path.append(L"EasyChat.ImeCompositionHook.log");

        InitOnceExecuteOnce(&g_logInit, InitializeLogMutex, nullptr, nullptr);
        const DWORD waitResult = g_logMutex != nullptr
            ? WaitForSingleObject(g_logMutex, 100)
            : WAIT_OBJECT_0;
        if (waitResult != WAIT_OBJECT_0 && waitResult != WAIT_ABANDONED)
            return;

        const HANDLE file = CreateFileW(
            path.c_str(),
            FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (file != INVALID_HANDLE_VALUE)
        {
            DWORD written = 0;
            WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
            CloseHandle(file);
        }

        if (g_logMutex != nullptr)
            ReleaseMutex(g_logMutex);
    }

    std::wstring ReadCompositionString(HIMC context, DWORD index)
    {
        const LONG byteCount = ImmGetCompositionStringW(context, index, nullptr, 0);
        if (byteCount <= 0)
            return {};

        std::vector<wchar_t> buffer(static_cast<size_t>(byteCount) / sizeof(wchar_t) + 1);
        const LONG copied = ImmGetCompositionStringW(
            context,
            index,
            buffer.data(),
            static_cast<DWORD>(byteCount));
        if (copied <= 0)
            return {};

        return std::wstring(buffer.data(), static_cast<size_t>(copied) / sizeof(wchar_t));
    }

    std::wstring WindowClass(HWND window)
    {
        wchar_t className[256]{};
        const int length = GetClassNameW(window, className, ARRAYSIZE(className));
        return length > 0 ? std::wstring(className, length) : std::wstring();
    }

    bool IsPasswordControl(HWND window, const std::wstring& className)
    {
        if (_wcsicmp(className.c_str(), L"Edit") == 0)
        {
            const LONG_PTR style = GetWindowLongPtrW(window, GWL_STYLE);
            if ((style & ES_PASSWORD) != 0)
                return true;
        }

        return _wcsicmp(className.c_str(), L"PasswordBox") == 0
            || _wcsicmp(className.c_str(), L"Chrome_WidgetWin_1") == 0 &&
               (GetWindowLongPtrW(window, GWL_STYLE) & ES_PASSWORD) != 0;
    }

    void LogImeMessage(const CWPSTRUCT& message)
    {
        if (message.hwnd == nullptr)
            return;

        const std::wstring className = WindowClass(message.hwnd);
        DWORD processId = 0;
        GetWindowThreadProcessId(message.hwnd, &processId);

        wchar_t numbers[512]{};
        SYSTEMTIME time{};
        GetLocalTime(&time);
        swprintf_s(
            numbers,
            L"timestamp=%04u-%02u-%02uT%02u:%02u:%02u.%03u hwnd=0x%p pid=%lu class=\"%s\" event=",
            time.wYear,
            time.wMonth,
            time.wDay,
            time.wHour,
            time.wMinute,
            time.wSecond,
            time.wMilliseconds,
            message.hwnd,
            processId,
            Escape(className).c_str());

        std::wstring line(numbers);
        if (message.message == WM_IME_STARTCOMPOSITION)
            line.append(L"WM_IME_STARTCOMPOSITION");
        else if (message.message == WM_IME_ENDCOMPOSITION)
            line.append(L"WM_IME_ENDCOMPOSITION");
        else
            line.append(L"WM_IME_COMPOSITION");

        if (message.message == WM_IME_COMPOSITION)
        {
            const DWORD flags = static_cast<DWORD>(message.lParam);
            wchar_t flagText[64]{};
            swprintf_s(flagText, L" flags=0x%08lX", flags);
            line.append(flagText);

            std::wstring composition;
            std::wstring result;
            const bool password = IsPasswordControl(message.hwnd, className);
            HIMC context = ImmGetContext(message.hwnd);
            if (context != nullptr)
            {
                if (!password && (flags == 0 || (flags & GCS_COMPSTR) != 0))
                    composition = ReadCompositionString(context, GCS_COMPSTR);
                if (!password && (flags == 0 || (flags & GCS_RESULTSTR) != 0))
                    result = ReadCompositionString(context, GCS_RESULTSTR);
                ImmReleaseContext(message.hwnd, context);
            }

            if (password)
                line.append(L" text=<redacted>");
            else
            {
                line.append(L" comp=\"");
                line.append(Escape(composition));
                line.append(L"\" result=\"");
                line.append(Escape(result));
                line.append(L"\"");
            }
        }

        WriteLog(line);
        OutputDebugStringW((line + L"\r\n").c_str());
    }

    LRESULT CALLBACK CallWndProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0 && lParam != 0)
        {
            const auto* message = reinterpret_cast<const CWPSTRUCT*>(lParam);
            if (message->message == WM_IME_STARTCOMPOSITION
                || message->message == WM_IME_COMPOSITION
                || message->message == WM_IME_ENDCOMPOSITION)
            {
                LogImeMessage(*message);
            }
        }

        return CallNextHookEx(nullptr, code, wParam, lParam);
    }
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_instance = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

BOOL WINAPI StartEasyChatImeHook()
{
    if (g_hook != nullptr)
        return TRUE;

    SetLastErrorValue(ERROR_SUCCESS);
    const HHOOK hook = SetWindowsHookExW(WH_CALLWNDPROC, CallWndProc, g_instance, 0);
    if (hook == nullptr)
    {
        SetLastErrorValue(GetLastError());
        wchar_t line[128]{};
        swprintf_s(line, L"event=hook.start_failed error=%lu", g_lastError);
        WriteLog(line);
        return FALSE;
    }

    g_hook = hook;
    WriteLog(L"event=hook.started kind=WH_CALLWNDPROC architecture=x64");
    return TRUE;
}

BOOL WINAPI StopEasyChatImeHook()
{
    const HHOOK hook = g_hook;
    if (hook == nullptr)
        return TRUE;

    g_hook = nullptr;

    const BOOL stopped = UnhookWindowsHookEx(hook);
    SetLastErrorValue(stopped ? ERROR_SUCCESS : GetLastError());
    WriteLog(stopped ? L"event=hook.stopped" : L"event=hook.stop_failed");
    return stopped;
}

DWORD WINAPI GetEasyChatImeHookLastError()
{
    return static_cast<DWORD>(InterlockedCompareExchange(&g_lastError, 0, 0));
}
