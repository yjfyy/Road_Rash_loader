Imports System.Threading

Partial Public Class Application
    ' 单实例互斥（仅主实例使用，--watch 不检查）
    Private Shared _mutex As New System.Threading.Mutex(True, "RoadRashLauncher_SingleInstance")

    Private Sub Application_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup
        If e.Args.Contains("--watch") Then
            RunWatchMode(e.Args)
            Shutdown()
            Return
        End If

        ' 单实例检查
        If Not _mutex.WaitOne(0) Then
            'MessageBox.Show("启动器已在运行。", "Road Rash Launcher", MessageBoxButton.OK, MessageBoxImage.Information)
            Shutdown()
            Return
        End If

        Dim main = New MainWindow()
        main.Show()
    End Sub

    ''' <summary>监视模式：主进程消失则结束游戏并恢复英文</summary>
    Private Sub RunWatchMode(args As String())
        Dim parentPid As Integer = 0
        For i As Integer = 0 To args.Length - 2
            If args(i) = "--parent" Then
                Integer.TryParse(args(i + 1), parentPid)
                Exit For
            End If
        Next

        Dim gameDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(IO.Path.DirectorySeparatorChar)
        Dim procName = "RoadRash95"

        ' 主进程不存在就直接恢复并退出
        Dim parent As Process = Nothing
        If parentPid > 0 Then
            Try
                parent = Process.GetProcessById(parentPid)
            Catch
            End Try
        End If

        If parent Is Nothing Then
            ResourceHelper.WriteLanguageDll(gameDir, "暴力摩托.ENU.DLL")
            Return
        End If

        ' 主进程消失后：结束游戏、恢复英文、退出。
        ' 游戏先退出时不在本进程恢复，避免每秒写文件；由主进程 Closing / Loaded 兜底。
        While True
            Try
                If parent.HasExited Then Exit While
            Catch
                Exit While
            End Try
            Thread.Sleep(200)
        End While

        ResourceHelper.KillGameProcess(procName)
        ResourceHelper.WriteLanguageDll(gameDir, "暴力摩托.ENU.DLL")
    End Sub
End Class