Imports System.IO
Imports System.Reflection
Imports Microsoft.Win32
Imports System.Security.Principal

Partial Public Class MainWindow
    Inherits Window

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
            ResourceHelper.WriteLanguageDll(GameDirectory, "暴力摩托.ENU.DLL")
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
            ResourceHelper.WriteLanguageDll(GameDirectory, "暴力摩托.ENU.DLL")
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
            UpdateStatus("游戏运行中 · LET'S RIDE!")

            ' 等游戏进程退出后再恢复按钮
            If _gameProcess IsNot Nothing Then
                Await Task.Run(Sub()
                                   Try
                                       _gameProcess.WaitForExit()
                                   Catch
                                   End Try
                               End Sub)
            End If

            UpdateStatus("游戏已退出 · READY")
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

        ' 中文/英文：写 TEXT\ENU.DLL；其他语言：不写
        If LangDllMap.ContainsKey(lang) Then
            'UpdateStatus("释放语言资源 · LANGUAGE DLL…")
            ExtractLanguageResource(lang, gameDir)
        End If

        'UpdateStatus("设置游戏语言 · LANGUAGE…")
        ApplyGameLanguage(lang)

        UpdateStatus("启动游戏 · LAUNCHING…")
        Dim exePath = Path.Combine(gameDir, GameExeName)
        If Not File.Exists(exePath) Then
            Throw New FileNotFoundException("未找到游戏可执行文件")
        End If

        Try
            _gameProcess?.Dispose()
        Catch
        End Try
        _gameProcess = Process.Start(New ProcessStartInfo(exePath, GameArgs) With {
        .WorkingDirectory = gameDir,
        .UseShellExecute = True
    })

        If _watchProcess Is Nothing OrElse _watchProcess.HasExited Then
            StartWatchProcess()
        End If

        If Not _watchdogStarted Then
            _watchdogStarted = True
            WatchWatchdog()
        End If
    End Sub

    Private Sub ExtractLanguageResource(langCode As String, gameDir As String)
        Dim resName = LangDllMap(langCode)
        Dim asm = Assembly.GetExecutingAssembly()

        Using stream = asm.GetManifestResourceStream(resName)
            If stream Is Nothing Then
                Throw New FileNotFoundException(
                "未找到嵌入资源：" & resName &
                "。当前资源：" & String.Join(", ", asm.GetManifestResourceNames()))
            End If

            Dim textDir = Path.Combine(gameDir, "TEXT")
            If Not Directory.Exists(textDir) Then Directory.CreateDirectory(textDir)

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

        ' ---- 写 HKCU 下的玩家名称 ----
        Try
            Using userKey = Registry.CurrentUser.CreateSubKey("Software\Electronic Arts\RoadRash 95")
                If userKey IsNot Nothing Then
                    Dim nameVal = TryCast(userKey.GetValue("Name", Nothing), String)
                    If String.IsNullOrWhiteSpace(nameVal) Then
                        userKey.SetValue("Name", "疯兔游戏fengtu.org", RegistryValueKind.String)
                    End If
                End If
            End Using
        Catch
            ' 写 HKCU 失败不影响启动流程
        End Try
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
                         System.Threading.Thread.Sleep(500)
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
    Private Sub Hyperlink_RequestNavigate(sender As Object, e As System.Windows.Navigation.RequestNavigateEventArgs)
        Try
            Process.Start(New ProcessStartInfo(e.Uri.AbsoluteUri) With {
                .UseShellExecute = True
            })
        Catch
        End Try
        e.Handled = True
    End Sub

    Private Sub Hyperlink_MouseEnter(sender As Object, e As MouseEventArgs)
        If TypeOf sender Is System.Windows.Documents.Hyperlink Then
            DirectCast(sender, System.Windows.Documents.Hyperlink).TextDecorations =
                System.Windows.TextDecorations.Underline
        End If
    End Sub

    Private Sub Hyperlink_MouseLeave(sender As Object, e As MouseEventArgs)
        If TypeOf sender Is System.Windows.Documents.Hyperlink Then
            DirectCast(sender, System.Windows.Documents.Hyperlink).TextDecorations = Nothing
        End If
    End Sub
End Class