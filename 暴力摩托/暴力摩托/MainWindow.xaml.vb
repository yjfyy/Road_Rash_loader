Imports System.IO
Imports System.Reflection
Imports Microsoft.Win32
Imports System.Security.Principal
Imports System.Runtime.InteropServices

Partial Public Class MainWindow
    Inherits Window
    ' ========== DLL 注入 P/Invoke ==========
    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function OpenProcess(dwDesiredAccess As Integer, bInheritHandle As Boolean, dwProcessId As Integer) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function VirtualAllocEx(hProcess As IntPtr, lpAddress As IntPtr, dwSize As UIntPtr, flAllocationType As Integer, flProtect As Integer) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WriteProcessMemory(hProcess As IntPtr, lpBaseAddress As IntPtr, lpBuffer As Byte(), nSize As UIntPtr, ByRef lpNumberOfBytesWritten As UIntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Ansi)>
    Private Shared Function GetModuleHandle(lpModuleName As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Ansi)>
    Private Shared Function GetProcAddress(hModule As IntPtr, lpProcName As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CreateRemoteThread(hProcess As IntPtr, lpThreadAttributes As IntPtr, dwStackSize As UIntPtr, lpStartAddress As IntPtr, lpParameter As IntPtr, dwCreationFlags As Integer, ByRef lpThreadId As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WaitForSingleObject(hHandle As IntPtr, dwMilliseconds As Integer) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(hObject As IntPtr) As Boolean
    End Function

    Private Const PROCESS_ALL_ACCESS As Integer = &H1F0FFF
    Private Const MEM_COMMIT As Integer = &H1000
    Private Const PAGE_READWRITE As Integer = &H4

    ''' <summary>把 Hook DLL 注入到目标进程</summary>
    Private Function InjectHookDll(pid As Integer, dllPath As String) As Boolean
        Dim hProcess = OpenProcess(PROCESS_ALL_ACCESS, False, pid)
        If hProcess = IntPtr.Zero Then Return False

        Try
            Dim pathBytes = System.Text.Encoding.Unicode.GetBytes(dllPath & vbNullChar)
            Dim pRemoteMem = VirtualAllocEx(hProcess, IntPtr.Zero,
            New UIntPtr(CULng(pathBytes.Length)), MEM_COMMIT, PAGE_READWRITE)
            If pRemoteMem = IntPtr.Zero Then Return False

            Dim written As UIntPtr
            If Not WriteProcessMemory(hProcess, pRemoteMem, pathBytes,
            New UIntPtr(CULng(pathBytes.Length)), written) Then Return False

            Dim hKernel32 = GetModuleHandle("kernel32.dll")
            Dim pLoadLibrary = GetProcAddress(hKernel32, "LoadLibraryW")

            Dim threadId As IntPtr
            Dim hThread = CreateRemoteThread(hProcess, IntPtr.Zero, UIntPtr.Zero,
            pLoadLibrary, pRemoteMem, 0, threadId)
            If hThread = IntPtr.Zero Then Return False

            WaitForSingleObject(hThread, 5000)
            CloseHandle(hThread)
            Return True
        Finally
            CloseHandle(hProcess)
        End Try
    End Function
    ' =======================================



    ' ========== 配置区 ==========
    Private Const GameExeName As String = "RoadRash95.exe"
    Private Const GameArgs As String = "-fullscreen"

    Private Const RegSubKey As String = "Software\RoadRashLauncher"
    Private Const GameLangKey As String = "SOFTWARE\WOW6432Node\Electronic Arts\RoadRash 95"

    Private Shared ReadOnly LangValueMap As New Dictionary(Of String, String) From {
        {"CHS", "English"}, {"ENU", "English"},
        {"DEU", "German"}, {"ESP", "Spanish"},
        {"FRA", "French"}, {"ITA", "Italian"}
    }

    Private Shared ReadOnly LangDllMap As New Dictionary(Of String, String) From {
        {"CHS", "暴力摩托.CHS.DLL"},
        {"ENU", "暴力摩托.ENU.DLL"}
    }

    Private ReadOnly _isAdmin As Boolean = CheckAdmin()

    Private _gameProcess As Process
    Private _watchProcess As Process
    Private _watchRestarting As Boolean = False
    Private _watchRestartCount As Integer = 0
    Private Const MaxWatchRestarts As Integer = 10
    Private _exiting As Boolean = False
    Private _closing As Boolean = False
    Private _watchdogStarted As Boolean = False
    ' ============================

    Public Sub New()
        InitializeComponent()
        AddHandler Loaded, AddressOf MainWindow_Loaded
    End Sub

    Private Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs)
        BuildRays()

        ' 启动即恢复英文，作为所有守护失效后的兜底
        Try
            ResourceHelper.RestoreEnglishDll(GameDirectory)
        Catch
        End Try
    End Sub

    Private Sub BuildRays()
        If RaysCanvas Is Nothing Then Return
        RaysCanvas.Children.Clear()

        Dim cx = Me.Width / 2
        Dim cy = Me.Height * 0.58
        Dim ink As New SolidColorBrush(Color.FromRgb(&HA, &HA, &HA))

        For i As Integer = 0 To 21
            Dim rad = i * (360.0 / 22) * Math.PI / 180.0
            RaysCanvas.Children.Add(New System.Windows.Shapes.Line() With {
                .X1 = cx, .Y1 = cy,
                .X2 = cx + Math.Cos(rad) * 900,
                .Y2 = cy + Math.Sin(rad) * 900,
                .Stroke = ink, .StrokeThickness = 26
            })
        Next
    End Sub

    ' ---------- 窗口拖动 / 系统按钮 ----------
    Private Sub TitleBar_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        If e.ButtonState = MouseButtonState.Pressed Then Me.DragMove()
    End Sub

    Private Sub MinBtn_Click(sender As Object, e As RoutedEventArgs)
        Me.WindowState = System.Windows.WindowState.Minimized
    End Sub

    Private Sub CloseBtn_Click(sender As Object, e As RoutedEventArgs)
        Me.Close()
    End Sub

    Private Sub MainWindow_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles Me.Closing
        If _closing Then Return
        _closing = True
        _exiting = True

        ' 先结束 --watch，避免它与本进程同时写 ENU.DLL
        Try
            If _watchProcess IsNot Nothing AndAlso Not _watchProcess.HasExited Then
                _watchProcess.Kill()
                _watchProcess.WaitForExit(3000)
            End If
        Catch
        End Try

        ' 结束游戏进程并恢复英文
        Try
            Dim pid = If(_gameProcess IsNot Nothing, _gameProcess.Id, 0)
            ResourceHelper.KillGameProcess(Path.GetFileNameWithoutExtension(GameExeName), pid)
            ResourceHelper.RestoreEnglishDll(GameDirectory)
        Catch
        End Try
    End Sub

    ' ---------- 启动流程 ----------
    Private Async Sub Launch_Click(sender As Object, e As RoutedEventArgs)
        If Not LaunchBtn.IsEnabled Then Return

        Dim lang = GetSelectedLanguage()
        LaunchBtn.IsEnabled = False
        LangCombo.IsEnabled = False

        Try
            _exiting = False
            _closing = False
            _watchRestarting = False
            _watchRestartCount = 0

            Await Task.Run(Sub() FullSetupAndLaunch(lang))
            UpdateStatus("完成 · LET'S RIDE!")
        Catch ex As Exception
            UpdateStatus("错误：" & ex.Message)
            MessageBox.Show(ex.ToString(), "启动失败", MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            LaunchBtn.IsEnabled = True
            LangCombo.IsEnabled = True
        End Try
    End Sub

    Private Function GetSelectedLanguage() As String
        Dim item = TryCast(LangCombo.SelectedItem, ComboBoxItem)
        Return If(item?.Tag?.ToString(), "ENU")
    End Function

    Private Sub FullSetupAndLaunch(lang As String)
        Dim gameDir = GameDirectory

        UpdateStatus("启动游戏 · LAUNCHING…")
        Dim exePath = Path.Combine(gameDir, GameExeName)
        If Not File.Exists(exePath) Then
            Throw New FileNotFoundException("未找到游戏可执行文件：" & exePath)
        End If

        Try
            _gameProcess?.Dispose()
        Catch
        End Try

        ' 选中文设置环境变量，代理 ENU.dll 会读它决定加载 CHS.dll
        If lang = "CHS" Then
            Environment.SetEnvironmentVariable("ROADRASH_LANG", "CHS", EnvironmentVariableTarget.Process)
        Else
            Environment.SetEnvironmentVariable("ROADRASH_LANG", Nothing, EnvironmentVariableTarget.Process)
        End If

        ' UseShellExecute=False 确保环境变量随子进程继承
        _gameProcess = Process.Start(New ProcessStartInfo(exePath, GameArgs) With {
    .WorkingDirectory = gameDir,
    .UseShellExecute = False
})



        'Dim gameDir = GameDirectory
        'If lang = "CHS" Then
        '    Dim hookDll = Path.Combine(gameDir, "LangHook.dll")
        '    If File.Exists(hookDll) Then
        '        Threading.Thread.Sleep(1500)  ' 等游戏初始化，避免太早注入失败
        '        InjectHookDll(_gameProcess.Id, hookDll)
        '    End If
        'End If

        'UpdateStatus("释放资源 · EXTRACTING…")
        'ExtractBaseResources(gameDir)

        'If LangDllMap.ContainsKey(lang) Then
        '    UpdateStatus("释放语言资源 · LANGUAGE DLL…")
        '    ExtractLanguageResource(lang, gameDir)
        'End If

        'UpdateStatus("写入记录 · REGISTRY…")
        'WriteLauncherRegistry(gameDir, lang)

        'UpdateStatus("设置游戏语言 · LANGUAGE…")
        'ApplyGameLanguage(lang)

        'UpdateStatus("启动游戏 · LAUNCHING…")
        'Dim exePath = Path.Combine(gameDir, GameExeName)
        'If Not File.Exists(exePath) Then
        '    Throw New FileNotFoundException("未找到游戏可执行文件：" & exePath)
        'End If

        '' 释放旧的进程对象
        'Try
        '    _gameProcess?.Dispose()
        'Catch
        'End Try
        '_gameProcess = Process.Start(New ProcessStartInfo(exePath, GameArgs) With {
        '    .WorkingDirectory = gameDir,
        '    .UseShellExecute = True
        '})

        '' 每次启动游戏都确保 --watch 存活
        'If _watchProcess Is Nothing OrElse _watchProcess.HasExited Then
        '    StartWatchProcess()
        'End If

        '' 看门狗只启动一次
        'If Not _watchdogStarted Then
        '    _watchdogStarted = True
        '    WatchWatchdog()
        'End If
    End Sub

    ' ---------- 资源释放 ----------
    Private Sub ExtractBaseResources(targetDir As String)
        Dim asm = Assembly.GetExecutingAssembly()
        Dim prefix = asm.GetName().Name & "."

        For Each resName In asm.GetManifestResourceNames()
            If Not resName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then Continue For

            Dim lower = resName.ToLowerInvariant()
            If lower.EndsWith(".chs.dll") OrElse lower.EndsWith(".enu.dll") Then Continue For

            Dim relative = resName.Substring(prefix.Length)
            Dim lastDot = relative.LastIndexOf("."c)
            If lastDot <= 0 Then Continue For

            Dim fileName = relative.Substring(0, lastDot).Replace("."c, Path.DirectorySeparatorChar) &
                           relative.Substring(lastDot)

            Dim destPath = Path.Combine(targetDir, fileName)
            If File.Exists(destPath) Then Continue For

            Dim destFolder = Path.GetDirectoryName(destPath)
            If Not Directory.Exists(destFolder) Then Directory.CreateDirectory(destFolder)

            Using input = asm.GetManifestResourceStream(resName)
                Using output = File.Create(destPath)
                    input.CopyTo(output)
                End Using
            End Using
        Next
    End Sub

    Private Sub ExtractLanguageResource(langCode As String, gameDir As String)
        Dim asm = Assembly.GetExecutingAssembly()
        Dim resName = LangDllMap(langCode)
        Dim stream As Stream = asm.GetManifestResourceStream(resName)

        If stream Is Nothing Then
            Dim fileName = Path.GetFileName(resName)
            resName = asm.GetManifestResourceNames().
                FirstOrDefault(Function(n) n.EndsWith("." & fileName, StringComparison.OrdinalIgnoreCase))
            If resName IsNot Nothing Then
                stream = asm.GetManifestResourceStream(resName)
            End If
        End If

        If stream Is Nothing Then
            Throw New FileNotFoundException(
                "未找到嵌入资源：" & LangDllMap(langCode) &
                "。当前资源：" & String.Join(", ", asm.GetManifestResourceNames()))
        End If

        Dim textDir = Path.Combine(gameDir, "TEXT")
        If Not Directory.Exists(textDir) Then Directory.CreateDirectory(textDir)

        Using stream
            Using output = File.Create(Path.Combine(textDir, "ENU.DLL"))
                stream.CopyTo(output)
            End Using
        End Using
    End Sub

    ' ---------- 注册表 ----------
    Private Sub ApplyGameLanguage(langCode As String)
        If Not _isAdmin Then
            Throw New UnauthorizedAccessException(
                "修改游戏语言需要管理员权限，请以管理员身份运行启动器。")
        End If

        Dim value = If(LangValueMap.ContainsKey(langCode), LangValueMap(langCode), langCode)

        Using baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            Using gameKey = baseKey.CreateSubKey(GameLangKey, RegistryKeyPermissionCheck.ReadWriteSubTree)
                If gameKey Is Nothing Then
                    Throw New InvalidOperationException("无法创建或打开注册表键：HKLM\" & GameLangKey)
                End If

                Dim existing = gameKey.GetValue("Language", Nothing)
                If existing Is Nothing OrElse existing.ToString() <> value Then
                    gameKey.SetValue("Language", value, RegistryValueKind.String)
                End If
            End Using
        End Using
    End Sub

    Private Sub WriteLauncherRegistry(gameDir As String, lang As String)
        Using key = Registry.CurrentUser.CreateSubKey(RegSubKey)
            key.SetValue("InstallDir", gameDir)
            key.SetValue("Version", "1.0")
            key.SetValue("Language", lang)
        End Using
    End Sub

    ' ---------- --watch 守护 ----------
    Private Sub StartWatchProcess()
        Try
            Try
                _watchProcess?.Dispose()
            Catch
            End Try

            Dim selfExe = Process.GetCurrentProcess().MainModule.FileName
            _watchProcess = Process.Start(New ProcessStartInfo(
                selfExe, "--watch --parent " & Process.GetCurrentProcess().Id) With {
                .UseShellExecute = True,
                .WindowStyle = ProcessWindowStyle.Hidden
            })
        Catch
            _watchProcess = Nothing
        End Try
    End Sub

    Private Sub WatchWatchdog()
        Task.Run(Sub()
                     While Not _exiting
                         System.Threading.Thread.Sleep(2000)
                         If _exiting Then Exit While

                         Dim needRestart As Boolean = False
                         Try
                             needRestart = (_watchProcess Is Nothing) OrElse _watchProcess.HasExited
                         Catch
                             needRestart = True
                         End Try

                         If needRestart Then
                             If _watchRestartCount >= MaxWatchRestarts Then
                                 UpdateStatus("守护进程重启次数超限，已停止自动重启")
                                 Exit While
                             End If

                             If Not _watchRestarting Then
                                 _watchRestarting = True
                                 _watchRestartCount += 1
                                 UpdateStatus("守护进程已重启（" & _watchRestartCount & "）")
                                 StartWatchProcess()
                                 _watchRestarting = False
                             End If
                         End If
                     End While
                 End Sub)
    End Sub

    ' ---------- 工具方法 ----------
    Private ReadOnly Property GameDirectory As String
        Get
            Return AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
        End Get
    End Property

    Private Shared Function CheckAdmin() As Boolean
        Using identity = WindowsIdentity.GetCurrent()
            Return New WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
        End Using
    End Function

    Private Sub UpdateStatus(msg As String)
        If Dispatcher.CheckAccess() Then
            StatusText.Text = msg
        Else
            Dispatcher.Invoke(Sub() StatusText.Text = msg)
        End If
    End Sub
End Class