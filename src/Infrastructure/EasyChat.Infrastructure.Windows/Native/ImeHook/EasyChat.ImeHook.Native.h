#pragma once

#include <windows.h>

#ifdef EASYCHAT_IME_HOOK_EXPORTS
#define EASYCHAT_IME_HOOK_API __declspec(dllexport)
#else
#define EASYCHAT_IME_HOOK_API __declspec(dllimport)
#endif

extern "C"
{
    EASYCHAT_IME_HOOK_API BOOL WINAPI StartEasyChatImeHook();
    EASYCHAT_IME_HOOK_API BOOL WINAPI StopEasyChatImeHook();
    EASYCHAT_IME_HOOK_API DWORD WINAPI GetEasyChatImeHookLastError();
}
