#include "pch.h"
#include <windows.h>
#include <string>

static HMODULE g_realDll = nullptr;

// 取当前代理 DLL 所在目录（TEXT\）
static std::wstring GetModuleDir(HMODULE hModule)
{
    wchar_t path[MAX_PATH] = { 0 };
    GetModuleFileNameW(hModule, path, MAX_PATH);
    std::wstring s(path);
    size_t pos = s.find_last_of(L"\\/");
    return (pos != std::wstring::npos) ? s.substr(0, pos + 1) : L"";
}

static HMODULE LoadRealDll(HMODULE hSelf)
{
    std::wstring dir = GetModuleDir(hSelf);

    wchar_t envBuf[16] = { 0 };
    DWORD n = GetEnvironmentVariableW(L"ROADRASH_LANG", envBuf, 16);

    std::wstring dllName;
    if (n > 0 && _wcsicmp(envBuf, L"CHS") == 0)
        dllName = L"CHS.dll";
    else
        dllName = L"ENU_real.dll";

    // 显式用绝对路径加载，避免受当前工作目录影响
    return LoadLibraryW((dir + dllName).c_str());
}

static FARPROC GetRealProc(const char* name)
{
    if (!g_realDll) return nullptr;
    return GetProcAddress(g_realDll, name);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
        g_realDll = LoadRealDll(hModule);
        if (!g_realDll) return FALSE;
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        if (g_realDll)
        {
            FreeLibrary(g_realDll);
            g_realDll = nullptr;
        }
    }
    return TRUE;
}

// ===== 导出转发函数（按实际导出表补全）=====
extern "C" __declspec(dllexport) void LangInit()
{
    using Fn = void(*)();
    auto fn = (Fn)GetRealProc("LangInit");
    if (fn) fn();
}

extern "C" __declspec(dllexport) const char* LangGetString(int id)
{
    using Fn = const char* (*)(int);
    auto fn = (Fn)GetRealProc("LangGetString");
    return fn ? fn(id) : nullptr;
}

extern "C" __declspec(dllexport) void LangSetMode(int mode)
{
    using Fn = void(*)(int);
    auto fn = (Fn)GetRealProc("LangSetMode");
    if (fn) fn(mode);
}

// ……按 dumpbin /exports TEXT\ENU_real.dll 的实际输出补全每个函数