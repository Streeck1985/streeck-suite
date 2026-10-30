// streeck_taskbar.dll: se carga dentro de explorer.exe y le da el skin a la barra de tareas REAL de Windows 11.
//
// Cómo entra: la suite pone un gancho de ventana (SetWindowsHookEx) en el hilo de la barra; Windows carga esta DLL
// en el Explorador, la DLL se fija para no descargarse y pide a Windows una sesión de diagnóstico XAML
// (InitializeXamlDiagnosticsEx, la API oficial que usa Visual Studio). Windows crea entonces nuestro objeto TAP y
// le entrega el árbol visual de la barra: así se pueden cambiar márgenes, fondos y esquinas de las piezas reales.
// Mismo mecanismo que TranslucentTB (ExplorerTAP) y el Taskbar Styler de Windhawk, escrito desde cero.
//
// La suite le habla por archivos y un evento con nombre:
//   %LOCALAPPDATA%\StreeckSuite\barra_skin.ini   ajustes (transparente=1, arbol=1)
//   Local\StreeckSuite.TaskbarSkin               evento: vuelve a leer los ajustes y los aplica
//   barra_skin.log / barra_arbol.txt             registro y volcado del árbol de piezas

#include <windows.h>
#include <unknwn.h>
#include <ocidl.h>
#include <xamlom.h>
#undef GetCurrentTime // Choca con Storyboard.GetCurrentTime de C++/WinRT
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.h>
#include <cstdio>
#include <fstream>
#include <map>
#include <string>
#include <thread>
#include <vector>

namespace wux = winrt::Windows::UI::Xaml;

// {E3A1B6C2-5D47-4F2A-9C3E-7B1D2F8A6E41}
static const CLSID CLSID_StreeckTap = { 0xe3a1b6c2, 0x5d47, 0x4f2a, { 0x9c, 0x3e, 0x7b, 0x1d, 0x2f, 0x8a, 0x6e, 0x41 } };

static HMODULE g_module;
static winrt::com_ptr<IXamlDiagnostics> g_diagnostics;

struct Node
{
    std::wstring Type, Name;
    InstanceHandle Parent;
};

// Solo se tocan desde el hilo de la interfaz de la barra (los avisos del árbol llegan ahí)
static std::map<InstanceHandle, Node> g_nodes;
static std::map<InstanceHandle, double> g_originalOpacity;   // Fondos de la barra con su opacidad original
static winrt::weak_ref<wux::UIElement> g_anchor;             // Cualquier pieza de la barra, para llegar a su hilo

#pragma region Utilidades

static std::wstring DataFolder()
{
    wchar_t buffer[MAX_PATH];
    DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", buffer, MAX_PATH);
    return std::wstring(buffer, length) + L"\\StreeckSuite";
}

static void Log(const std::wstring& text)
{
    SYSTEMTIME now;
    GetLocalTime(&now);
    wchar_t stamp[32];
    swprintf_s(stamp, L"%02d:%02d:%02d.%03d ", now.wHour, now.wMinute, now.wSecond, now.wMilliseconds);
    std::wofstream file(DataFolder() + L"\\barra_skin.log", std::ios::app);
    file << stamp << text << L"\n";
}

static std::wstring ReadSetting(const std::wstring& key, const std::wstring& fallback)
{
    std::wifstream file(DataFolder() + L"\\barra_skin.ini");
    std::wstring line;
    while (std::getline(file, line))
    {
        if (line.size() > key.size() && line.compare(0, key.size(), key) == 0 && line[key.size()] == L'=')
        {
            std::wstring value = line.substr(key.size() + 1);
            while (!value.empty() && (value.back() == L'\r' || value.back() == L' ')) value.pop_back();
            return value;
        }
    }
    return fallback;
}

static wux::UIElement ElementFrom(InstanceHandle handle)
{
    winrt::Windows::Foundation::IInspectable object;
    if (!g_diagnostics || FAILED(g_diagnostics->GetIInspectableFromHandle(handle, reinterpret_cast<::IInspectable**>(winrt::put_abi(object))))) return nullptr;
    return object.try_as<wux::UIElement>();
}

// ¿Está la pieza dentro de una barra (Taskbar.TaskbarFrame)?
static bool InsideTaskbarFrame(InstanceHandle handle)
{
    for (int depth = 0; depth < 64; depth++)
    {
        auto it = g_nodes.find(handle);
        if (it == g_nodes.end()) return false;
        if (it->second.Type == L"Taskbar.TaskbarFrame") return true;
        handle = it->second.Parent;
    }
    return false;
}

#pragma endregion

#pragma region Aplicar el skin

static void DumpTree()
{
    std::map<InstanceHandle, std::vector<InstanceHandle>> children;
    std::vector<InstanceHandle> roots;
    for (auto& pair : g_nodes)
    {
        if (g_nodes.count(pair.second.Parent)) children[pair.second.Parent].push_back(pair.first);
        else roots.push_back(pair.first);
    }
    std::wofstream file(DataFolder() + L"\\barra_arbol.txt");
    std::vector<std::pair<InstanceHandle, int>> stack;
    for (auto it = roots.rbegin(); it != roots.rend(); ++it) stack.push_back({ *it, 0 });
    while (!stack.empty())
    {
        auto item = stack.back();
        stack.pop_back();
        const Node& node = g_nodes[item.first];
        file << std::wstring(item.second * 2, L' ') << node.Type;
        if (!node.Name.empty()) file << L"#" << node.Name;
        file << L"\n";
        auto& list = children[item.first];
        for (auto it = list.rbegin(); it != list.rend(); ++it) stack.push_back({ *it, item.second + 1 });
    }
    Log(L"Árbol guardado: " + std::to_wstring(g_nodes.size()) + L" piezas");
}

static void StyleBackground(InstanceHandle handle)
{
    wux::UIElement element = ElementFrom(handle);
    if (!element) return;
    if (!g_originalOpacity.count(handle)) g_originalOpacity[handle] = element.Opacity();
    bool transparent = ReadSetting(L"transparente", L"0") == L"1";
    element.Opacity(transparent ? 0.0 : g_originalOpacity[handle]);
}

static void ApplyAll()
{
    try
    {
        for (auto& pair : g_originalOpacity) StyleBackground(pair.first);
        if (ReadSetting(L"arbol", L"0") == L"1") DumpTree();
        Log(L"Ajustes aplicados");
    }
    catch (winrt::hresult_error const& error)
    {
        Log(L"Error al aplicar: " + std::wstring(error.message()));
    }
}

#pragma endregion

#pragma region Árbol visual

struct Watcher : winrt::implements<Watcher, IVisualTreeServiceCallback2>
{
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutation) noexcept override
    {
        try
        {
            if (mutation == Add)
            {
                Node node;
                node.Type = element.Type ? std::wstring(element.Type, SysStringLen(element.Type)) : L"";
                node.Name = element.Name ? std::wstring(element.Name, SysStringLen(element.Name)) : L"";
                node.Parent = relation.Parent;
                g_nodes[element.Handle] = node;

                if (node.Type == L"Taskbar.TaskbarFrame" && !g_anchor.get())
                {
                    wux::UIElement frame = ElementFrom(element.Handle);
                    if (frame) { g_anchor = frame; Log(L"Barra encontrada"); }
                }
                if (node.Type == L"Windows.UI.Xaml.Shapes.Rectangle" && (node.Name == L"BackgroundFill" || node.Name == L"BackgroundStroke") &&
                    InsideTaskbarFrame(element.Handle))
                {
                    StyleBackground(element.Handle);
                }
            }
            else if (mutation == Remove)
            {
                g_nodes.erase(element.Handle);
                g_originalOpacity.erase(element.Handle);
            }
        }
        catch (...)
        {
        }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) noexcept override
    {
        return S_OK;
    }
};

// Espera el aviso de la suite y aplica los ajustes en el hilo de la barra
static void ListenForSettings()
{
    HANDLE changed = CreateEventW(nullptr, FALSE, FALSE, L"Local\\StreeckSuite.TaskbarSkin");
    for (;;)
    {
        WaitForSingleObject(changed, INFINITE);
        wux::UIElement anchor = g_anchor.get();
        if (!anchor) { Log(L"Aviso recibido pero la barra aún no aparece"); continue; }
        anchor.Dispatcher().RunAsync(winrt::Windows::UI::Core::CoreDispatcherPriority::Normal, [] { ApplyAll(); });
    }
}

struct Tap : winrt::implements<Tap, IObjectWithSite>
{
    winrt::com_ptr<IUnknown> m_site;

    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) noexcept override
    {
        try
        {
            m_site.copy_from(site);
            if (!site) return S_OK;
            g_diagnostics = m_site.as<IXamlDiagnostics>();
            winrt::com_ptr<IVisualTreeService3> service = m_site.as<IVisualTreeService3>();
            winrt::com_ptr<Watcher> watcher = winrt::make_self<Watcher>();
            // Igual que TranslucentTB: avisar desde otro hilo evita bloqueos; los avisos llegan al hilo de la barra
            std::thread([service, watcher]
            {
                HRESULT hr = service->AdviseVisualTreeChange(watcher.get());
                Log(SUCCEEDED(hr) ? L"Conectado al árbol de la barra" : L"AdviseVisualTreeChange falló: " + std::to_wstring(hr));
            }).detach();
            std::thread(ListenForSettings).detach();
            return S_OK;
        }
        catch (...)
        {
            return winrt::to_hresult();
        }
    }

    HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** site) noexcept override
    {
        if (!m_site) { *site = nullptr; return E_FAIL; }
        return m_site->QueryInterface(riid, site);
    }
};

struct Factory : winrt::implements<Factory, IClassFactory>
{
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** result) noexcept override
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        try
        {
            return winrt::make_self<Tap>()->QueryInterface(riid, result);
        }
        catch (...)
        {
            return winrt::to_hresult();
        }
    }

    HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override
    {
        return S_OK;
    }
};

#pragma endregion

#pragma region Entrada en el Explorador

typedef HRESULT(WINAPI* InitializeXamlDiagnosticsExFn)(LPCWSTR, DWORD, LPCWSTR, LPCWSTR, CLSID, LPCWSTR);

// Pide la sesión de diagnóstico XAML para este mismo proceso; reintenta mientras la barra aún no tiene XAML
static DWORD WINAPI Install(void*)
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(g_module, path, MAX_PATH);
    HMODULE xaml = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    InitializeXamlDiagnosticsExFn initialize = xaml ? reinterpret_cast<InitializeXamlDiagnosticsExFn>(GetProcAddress(xaml, "InitializeXamlDiagnosticsEx")) : nullptr;
    if (!initialize) { Log(L"No se encontró InitializeXamlDiagnosticsEx"); return 1; }

    HRESULT hr = E_FAIL;
    for (int attempt = 0; attempt < 60; attempt++)
    {
        wchar_t endpoint[64];
        swprintf_s(endpoint, L"VisualDiagConnection%d", attempt % 10 + 1);
        hr = initialize(endpoint, GetCurrentProcessId(), L"", path, CLSID_StreeckTap, nullptr);
        if (SUCCEEDED(hr)) break;
        Sleep(500);
    }
    Log(SUCCEEDED(hr) ? L"Diagnóstico XAML iniciado" : L"InitializeXamlDiagnosticsEx falló: " + std::to_wstring(hr));
    return 0;
}

static bool IsExplorer()
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    std::wstring exe(path);
    size_t slash = exe.find_last_of(L'\\');
    return _wcsicmp(exe.substr(slash + 1).c_str(), L"explorer.exe") == 0;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    g_module = instance;
    DisableThreadLibraryCalls(instance);
    if (IsExplorer())
    {
        // Fijada: el gancho que la trajo se quita enseguida y el árbol XAML guarda punteros a nuestro código
        HMODULE pinned;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, reinterpret_cast<LPCWSTR>(&DllMain), &pinned);
        HANDLE thread = CreateThread(nullptr, 0, Install, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}

#pragma endregion

// Exportaciones: la clase TAP para Windows y un gancho vacío que solo sirve para entrar en el Explorador
STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** result)
{
    if (clsid != CLSID_StreeckTap) return CLASS_E_CLASSNOTAVAILABLE;
    try
    {
        return winrt::make_self<Factory>()->QueryInterface(riid, result);
    }
    catch (...)
    {
        return winrt::to_hresult();
    }
}

STDAPI DllCanUnloadNow()
{
    return S_FALSE;
}

extern "C" LRESULT CALLBACK StreeckHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    return CallNextHookEx(nullptr, code, wParam, lParam);
}
