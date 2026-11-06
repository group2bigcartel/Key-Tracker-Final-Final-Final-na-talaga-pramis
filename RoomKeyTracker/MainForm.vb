Imports System.Drawing.Drawing2D
Imports System.Drawing.Printing
Imports System.Text.RegularExpressions
Imports AForge.Video
Imports AForge.Video.DirectShow
Imports QRCoder

Public Class MainForm

    Private ReadOnly GreenFg As Color = Color.FromArgb(21, 128, 61)
    Private ReadOnly GreenBg As Color = Color.FromArgb(220, 252, 231)
    Private ReadOnly RedFg As Color = Color.FromArgb(185, 28, 28)
    Private ReadOnly RedBg As Color = Color.FromArgb(254, 226, 226)

    Private ReadOnly Accent As Color = ColorTranslator.FromHtml("#256020")
    Private ReadOnly AccentDark As Color = ColorTranslator.FromHtml("#1F5E1A")
    Private ReadOnly AccentHover As Color = ColorTranslator.FromHtml("#2F7A29")
    Private ReadOnly PageBg As Color = ColorTranslator.FromHtml("#F1F7F0")
    Private ReadOnly Gold As Color = ColorTranslator.FromHtml("#C9A227")

    Private S As AppState = AppState.Load()

    Private pnlConfirm As FlowLayoutPanel
    Private _cam As VideoCaptureDevice
    Private _locked As Boolean
    Private _gotFrame As Boolean
    Private ReadOnly _camMonikers As New List(Of String)
    Private ReadOnly camWatch As New Timer With {.Interval = 6000}
    Private _step As Integer = 1
    Private _user As User
    Private _linkMode As Boolean
    Private _linkName As String = ""
    Private _linkRole As String = "FAC"
    Private btnResetPw As Button
    Private ReadOnly _reader As New ZXing.Windows.Compatibility.BarcodeReader()

    Private ReadOnly refresh As New Timer With {.Interval = 30000}

    Private _adminUnlocked As Boolean
    Private _prevTab As TabPage

    Private _schoolLogo As Bitmap
    Private _heimaLogo As Bitmap
    Private _roomsW As Integer
    Private _keysW As Integer

    Public Sub New()
        InitializeComponent()

        _reader.AutoRotate = True
        _reader.Options.TryHarder = True
        _reader.Options.TryInverted = True
        _reader.Options.PossibleFormats = New List(Of ZXing.BarcodeFormat) From {ZXing.BarcodeFormat.QR_CODE}

        pnlConfirm = New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown,
                                               .WrapContents = False, .Visible = False}
        tpScan.Controls.Add(pnlConfirm)

        cboRole.SelectedIndex = 0
        LoadCams()
        ApplyTheme()
        SetFullscreen(True)

        _prevTab = tpRooms
        AddHandler tabs.SelectedIndexChanged, AddressOf OnTabChanged
        AddHandler tabsAdmin.SelectedIndexChanged, Sub() DrawAdminTab()
        AddHandler refresh.Tick, Sub() If tabs.SelectedTab Is tpRooms Then DrawRooms()
        AddHandler camWatch.Tick,
            Sub()
                camWatch.Stop()
                If _cam IsNot Nothing AndAlso Not _gotFrame Then
                    ShowErr("No video received from the camera. It may be in use by another app, " &
                            "blocked in Windows Settings > Privacy > Camera (allow desktop apps), or not compatible.")
                    btnStart.Visible = True : btnStart.Text = "Start camera"
                End If
            End Sub
        refresh.Start()
        OnTabChanged(Nothing, EventArgs.Empty)
    End Sub

    Private _fullscreen As Boolean

    Private Sub SetFullscreen(enable As Boolean)
        _fullscreen = enable
        If enable Then
            Me.WindowState = FormWindowState.Normal
            Me.FormBorderStyle = FormBorderStyle.None
            Me.WindowState = FormWindowState.Maximized
        Else
            Me.FormBorderStyle = FormBorderStyle.Sizable
            Me.WindowState = FormWindowState.Normal
        End If
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If keyData = Keys.F11 Then
            SetFullscreen(Not _fullscreen)
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Private Sub ApplyTheme()
        Me.BackColor = PageBg

        _schoolLogo = BrandAssets.SchoolLogo()
        _heimaLogo = BrandAssets.HeimaLogo()

        ' Window / taskbar icon = the school seal
        Try
            Using small As Bitmap = BrandAssets.Scaled(_schoolLogo, 48)
                Me.Icon = System.Drawing.Icon.FromHandle(small.GetHicon())
            End Using
        Catch
        End Try

        ' ---- Header: school seal top-left (transparent PNG, so it blends into the green bar) ----
        Dim header As New Panel With {.Dock = DockStyle.Top, .Height = 64, .BackColor = AccentDark}
        Dim goldLine As New Panel With {.Dock = DockStyle.Bottom, .Height = 3, .BackColor = Gold}
        Dim logoHost As New Panel With {.Dock = DockStyle.Left, .Width = 76, .BackColor = AccentDark,
                                        .Padding = New Padding(12, 6, 4, 6)}
        logoHost.Controls.Add(New PictureBox With {.Dock = DockStyle.Fill, .Image = _schoolLogo,
                                                   .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = AccentDark})
        Dim textHost As New Panel With {.Dock = DockStyle.Fill, .BackColor = AccentDark,
                                        .Padding = New Padding(4, 6, 0, 0)}
        Dim lblSub As New Label With {.Text = "Isabela State University", .AutoSize = False, .Dock = DockStyle.Fill,
                                      .TextAlign = ContentAlignment.TopLeft, .BackColor = AccentDark,
                                      .ForeColor = Color.FromArgb(214, 232, 210), .Font = New Font("Segoe UI", 9.5F)}
        Dim title As New Label With {.Text = "Room Key Tracker", .ForeColor = Color.White, .AutoSize = False,
                                     .Dock = DockStyle.Top, .Height = 32, .TextAlign = ContentAlignment.BottomLeft,
                                     .BackColor = AccentDark, .Font = New Font("Segoe UI", 15, FontStyle.Bold)}
        textHost.Controls.Add(lblSub)
        textHost.Controls.Add(title)
        header.Controls.Add(textHost)   ' Fill goes in first, edge-docked panels after it
        header.Controls.Add(goldLine)
        header.Controls.Add(logoHost)
        Me.Controls.Add(header)
        header.SendToBack()

        ' ---- Footer: "Meet the team" button bottom-left, H-E-I-M-A credit bottom-right ----
        Dim navy As Color = Color.FromArgb(11, 16, 32)
        Dim footer As New Panel With {.Dock = DockStyle.Bottom, .Height = 60, .BackColor = navy}
        Dim footLine As New Panel With {.Dock = DockStyle.Top, .Height = 2, .BackColor = Gold}
        Dim footBody As New Panel With {.Dock = DockStyle.Fill, .BackColor = navy,
                                        .Padding = New Padding(12, 8, 16, 8)}
        Dim btnTeam As New Button With {.Text = "Meet the team", .Dock = DockStyle.Left, .Width = 150,
                                        .Font = New Font("Segoe UI", 10, FontStyle.Bold)}
        AddHandler btnTeam.Click, Sub() ShowTeam()

        Dim credit As New FlowLayoutPanel With {.Dock = DockStyle.Right, .AutoSize = True, .WrapContents = False,
                                                .FlowDirection = FlowDirection.LeftToRight, .BackColor = navy}
        Dim lines As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0),
                                               .FlowDirection = FlowDirection.TopDown, .BackColor = navy}
        lines.Controls.Add(New Label With {.Text = "Developed by H-E-I-M-A", .AutoSize = True,
                                           .ForeColor = Color.White, .BackColor = navy,
                                           .Font = New Font("Segoe UI", 10, FontStyle.Bold),
                                           .Margin = New Padding(0)})
        lines.Controls.Add(New Label With {.Text = "Group 2  ·  © 2026–2027", .AutoSize = True,
                                           .ForeColor = Color.FromArgb(148, 163, 184), .BackColor = navy,
                                           .Font = New Font("Segoe UI", 9.0F), .Margin = New Padding(0, 2, 0, 0)})
        credit.Controls.Add(New PictureBox With {.Image = _heimaLogo, .SizeMode = PictureBoxSizeMode.Zoom,
                                                 .Size = New Size(42, 42), .Margin = New Padding(0, 0, 10, 0),
                                                 .BackColor = navy})
        credit.Controls.Add(lines)
        footBody.Controls.Add(credit)
        footBody.Controls.Add(btnTeam)
        footer.Controls.Add(footBody)   ' Fill first
        footer.Controls.Add(footLine)
        Me.Controls.Add(footer)

        ' Register tab: admin can reset a user's password; list shows who has one
        btnResetPw = New Button With {.Text = "Reset password", .AutoSize = True,
                                      .Padding = New Padding(10, 4, 10, 4), .Margin = New Padding(3)}
        AddHandler btnResetPw.Click, AddressOf btnResetPw_Click
        flpRegRow.Controls.Add(btnResetPw)
        lvUsers.Columns.Add("Password", 100)

        ThemeControls(Me)

        ' Danger actions in red (applied after the theme so they are not turned green)
        For Each b As Button In New Button() {btnRemove, btnRemoveRoom, btnClearLog}
            b.BackColor = RedFg
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(153, 27, 27)
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(127, 29, 29)
        Next

        ' Enter key submits the forms
        AddHandler txtName.KeyDown,
            Sub(sender As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Enter Then
                    e.SuppressKeyPress = True
                    btnGenerate.PerformClick()
                End If
            End Sub
        AddHandler txtRoom.KeyDown,
            Sub(sender As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Enter Then
                    e.SuppressKeyPress = True
                    btnAddRoom.PerformClick()
                End If
            End Sub

        AddHandler flpKeys.SizeChanged,
            Sub(sender As Object, e As EventArgs)
                If IsHandleCreated AndAlso tabsAdmin.SelectedTab Is tpKeys AndAlso flpKeys.ClientSize.Width <> _keysW Then
                    DrawKeys()
                End If
            End Sub

        ' Rooms: re-flow the floor rows when the window size changes (F11, resize)
        AddHandler flpRooms.SizeChanged,
            Sub(sender As Object, e As EventArgs)
                If IsHandleCreated AndAlso tabs.SelectedTab Is tpRooms AndAlso flpRooms.ClientSize.Width <> _roomsW Then
                    DrawRooms()
                End If
            End Sub
    End Sub

    Private Sub ThemeControls(parent As Control)
        For Each c As Control In parent.Controls
            Dim b = TryCast(c, Button)
            If b IsNot Nothing Then
                b.FlatStyle = FlatStyle.Flat
                b.FlatAppearance.BorderSize = 0
                b.BackColor = Accent
                b.ForeColor = Color.White
                b.FlatAppearance.MouseOverBackColor = AccentHover
                b.FlatAppearance.MouseDownBackColor = AccentDark
                b.Cursor = Cursors.Hand
            ElseIf TypeOf c Is TabPage Then
                c.BackColor = PageBg
            End If
            ThemeControls(c)
        Next
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        StopCam()
        MyBase.OnFormClosing(e)
    End Sub

    Private _switching As Boolean

    Private Sub OnTabChanged(sender As Object, e As EventArgs)
        If _switching Then Return
        Dim cur = tabs.SelectedTab

        If cur IsNot tpScan Then StopCam() : _linkMode = False

        If _prevTab Is tpAdmin AndAlso cur IsNot tpAdmin AndAlso Not _linkMode Then _adminUnlocked = False

        If cur Is tpAdmin AndAlso Not _adminUnlocked Then
            If RequirePassword("open the Admin page") Then
                _adminUnlocked = True
            Else
                _switching = True
                tabs.SelectedTab = If(_prevTab IsNot Nothing AndAlso _prevTab IsNot tpAdmin, _prevTab, tpRooms)
                _switching = False
                cur = tabs.SelectedTab
                If cur IsNot tpScan Then StopCam()
            End If
        End If

        _prevTab = cur
        If cur Is tpRooms Then
            DrawRooms()
        ElseIf cur Is tpScan Then
            ResetScan()
        ElseIf cur Is tpLog Then
            DrawLog()
        ElseIf cur Is tpAdmin Then
            DrawAdminTab()
        End If
    End Sub

    Private Sub UpdateAdminCounts()
        tpRegister.Text = $"Register ({S.Users.Count})"
        tpKeys.Text = $"Key QRs ({S.Rooms.Count})"
    End Sub

    Private Sub DrawAdminTab()
        UpdateAdminCounts()
        If tabsAdmin.SelectedTab Is tpRegister Then
            DrawUsers()
        ElseIf tabsAdmin.SelectedTab Is tpKeys Then
            DrawKeys()
        End If
    End Sub

    Private Shared Function Fmt(t As DateTime?) As String
        Return If(t.HasValue, t.Value.ToString("MMM d, h:mm tt"), "")
    End Function

    Private Shared Function RoleName(r As String) As String
        Return If(r = "FAC", "Faculty", "OJT")
    End Function

    Private Shared Function MakeQr(text As String, pixelsPerModule As Integer) As Bitmap
        Using gen As New QRCodeGenerator()
            Using data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M)
                Using code As New QRCoder.QRCode(data)
                    Return code.GetGraphic(pixelsPerModule)
                End Using
            End Using
        End Using
    End Function

    Private Shared Sub ClearPanel(p As Control)
        For i = p.Controls.Count - 1 To 0 Step -1
            Dim c = p.Controls(i)
            DisposeImages(c)
            p.Controls.RemoveAt(i)
            c.Dispose()
        Next
    End Sub

    Private Shared Sub DisposeImages(c As Control)
        Dim pb = TryCast(c, PictureBox)
        If pb IsNot Nothing AndAlso pb.Image IsNot Nothing Then
            pb.Image.Dispose()
            pb.Image = Nothing
        End If
        For Each child As Control In c.Controls
            DisposeImages(child)
        Next
    End Sub

    Private Function NewLabel(text As String, Optional size As Single = 10, Optional bold As Boolean = False,
                              Optional color As Color = Nothing) As Label
        Dim l As New Label With {
            .Text = text, .AutoSize = True,
            .Font = New Font("Segoe UI", size, If(bold, FontStyle.Bold, FontStyle.Regular)),
            .Margin = New Padding(3, 4, 3, 4)}
        If color <> Nothing Then l.ForeColor = color
        Return l
    End Function

    Private Function NewButton(text As String, primary As Boolean) As Button
        Dim b As New Button With {.Text = text, .AutoSize = True, .Padding = New Padding(10, 4, 10, 4),
                                  .FlatStyle = FlatStyle.Flat, .Margin = New Padding(3)}
        If primary Then
            b.BackColor = Accent : b.ForeColor = Color.White
        End If
        Return b
    End Function

    Private Sub DrawRooms()
        Dim occ = S.Rooms.Where(Function(r) r.Holder IsNot Nothing).Count()
        lblVac.Text = $" {S.Rooms.Count - occ} vacant "
        lblOcc.Text = $" {occ} occupied "
        _roomsW = flpRooms.ClientSize.Width
        flpRooms.SuspendLayout()
        ClearPanel(flpRooms)
        flpRooms.FlowDirection = FlowDirection.TopDown
        flpRooms.WrapContents = False

        ' Floors are stacked like the building: the highest floor on top, floor 1 at the bottom.
        ' Rooms that are not plain numbers (e.g. "LAB-A") go in a last group.
        Dim usable As Integer = Math.Max(300, flpRooms.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12)
        Dim floors = S.Rooms.GroupBy(Function(rm) FloorOf(rm.Id)).
                             OrderBy(Function(grp) If(grp.Key = 0, Integer.MaxValue, -grp.Key))
        For Each fl In floors
            Dim head As Label = NewLabel(If(fl.Key = 0, "Other rooms", $"Floor {fl.Key}"), 11, True, Accent)
            head.Margin = New Padding(6, 10, 3, 0)
            flpRooms.Controls.Add(head)

            Dim row As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                 .WrapContents = True, .FlowDirection = FlowDirection.LeftToRight,
                                                 .MaximumSize = New Size(usable, 0), .Margin = New Padding(0)}
            For Each r In fl.OrderBy(Function(x) RoomNum(x.Id)).ThenBy(Function(x) x.Id)
                row.Controls.Add(RoomCard(r))
            Next
            flpRooms.Controls.Add(row)
        Next
        flpRooms.ResumeLayout()
    End Sub

    Private Shared Function RoomNum(id As String) As Integer
        Dim n As Integer
        If Integer.TryParse(id, n) AndAlso n > 0 Then Return n
        Return Integer.MaxValue
    End Function

    ' 101-199 -> 1, 201-299 -> 2, 301-399 -> 3. 0 = not a plain room number.
    Private Shared Function FloorOf(id As String) As Integer
        Dim n As Integer = RoomNum(id)
        If n = Integer.MaxValue OrElse n < 100 Then Return 0
        Return n \ 100
    End Function

    Private Function RoomCard(r As Room) As Panel
        Dim isOcc As Boolean = r.Holder IsNot Nothing
        Dim card As New Panel With {.Width = 170, .Height = 110, .Margin = New Padding(6),
            .BorderStyle = BorderStyle.FixedSingle,
            .BackColor = If(isOcc, RedBg, GreenBg)}
        Dim fg As Color = If(isOcc, RedFg, GreenFg)
        Dim detail As String
        If isOcc Then
            Dim u = S.Users.FirstOrDefault(Function(x) x.Id = r.Holder)
            detail = "OCCUPIED" & vbCrLf & If(u IsNot Nothing, u.Name, r.Holder) & vbCrLf & "since " & Fmt(r.Since)
        Else
            detail = "VACANT" & vbCrLf & "Key available"
        End If
        Dim d As New Label With {.Dock = DockStyle.Fill, .Text = detail, .ForeColor = fg, .Padding = New Padding(8, 0, 4, 4)}
        Dim t As New Label With {.Dock = DockStyle.Top, .Height = 36, .Text = "Room " & r.Id, .ForeColor = fg,
            .Font = New Font("Segoe UI", 14, FontStyle.Bold), .Padding = New Padding(8, 6, 0, 0)}
        card.Controls.Add(d)
        card.Controls.Add(t)
        Return card
    End Function

    ' ------------------------------------------------------------------
    ' Team dialog (opened by the "Meet the team" button in the footer)
    ' ------------------------------------------------------------------
    Private Sub ShowTeam()
        Dim navy As Color = Color.FromArgb(11, 16, 32)
        Using f As New Form With {.Text = "Meet the team", .FormBorderStyle = FormBorderStyle.FixedDialog,
                                  .StartPosition = FormStartPosition.CenterParent, .MaximizeBox = False,
                                  .MinimizeBox = False, .ShowInTaskbar = False, .BackColor = Color.White,
                                  .Font = Me.Font, .AutoScaleDimensions = New SizeF(96.0F, 96.0F),
                                  .AutoScaleMode = AutoScaleMode.Dpi, .ClientSize = New Size(460, 580)}
            ' Banner with the maker's logo and name
            Dim banner As New Panel With {.Dock = DockStyle.Top, .Height = 128, .BackColor = navy}
            banner.Controls.Add(New PictureBox With {.Image = _heimaLogo, .SizeMode = PictureBoxSizeMode.Zoom,
                                                     .Size = New Size(84, 84), .Location = New Point(24, 22),
                                                     .BackColor = navy})
            banner.Controls.Add(New Label With {.Text = "H-E-I-M-A", .AutoSize = True, .Location = New Point(124, 14),
                                                .Font = New Font("Segoe UI", 20, FontStyle.Bold),
                                                .ForeColor = Color.White, .BackColor = navy})
            banner.Controls.Add(New Label With {.Text = "Group 2", .AutoSize = True, .Location = New Point(128, 56),
                                                .Font = New Font("Segoe UI", 11, FontStyle.Bold),
                                                .ForeColor = Gold, .BackColor = navy})
            banner.Controls.Add(New Label With {.Text = "Room Key Tracker  ·  2026–2027" & vbCrLf & "Isabela State University",
                                                .AutoSize = True, .Location = New Point(128, 80),
                                                .ForeColor = Color.FromArgb(148, 163, 184), .BackColor = navy})

            ' Member cards
            Dim list As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown,
                                                  .WrapContents = False, .AutoScroll = True,
                                                  .Padding = New Padding(20, 12, 10, 6), .BackColor = Color.White}
            AddMember(list, "Trinidad, Andrae", "Project Manager")
            AddMember(list, "Somera, Marc", "UI Design")
            AddMember(list, "Clemente, Hasley Jay", "Backend")
            AddMember(list, "Gidden, Eryl", "Backend")
            AddMember(list, "Nuez, Caezar Ian", "Debugger/Tester")

            ' Bottom bar: copyright mark + Close
            Dim bar As New Panel With {.Dock = DockStyle.Bottom, .Height = 48, .BackColor = Color.White}
            Dim btnClose As New Button With {.Text = "Close", .DialogResult = DialogResult.OK, .Size = New Size(96, 32),
                                             .Location = New Point(344, 8), .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                                             .FlatStyle = FlatStyle.Flat, .BackColor = Accent, .ForeColor = Color.White}
            btnClose.FlatAppearance.BorderSize = 0
            bar.Controls.Add(New Label With {.Text = "© 2026–2027 H-E-I-M-A", .AutoSize = True,
                                             .Location = New Point(20, 14), .ForeColor = Color.FromArgb(90, 98, 112),
                                             .BackColor = Color.White})
            bar.Controls.Add(btnClose)
            f.AcceptButton = btnClose
            f.CancelButton = btnClose

            f.Controls.Add(list)
            f.Controls.Add(banner)
            f.Controls.Add(bar)

            ' On small screens keep the whole dialog (and the Close button) visible
            AddHandler f.Load,
                Sub(sender As Object, e As EventArgs)
                    Dim wa As Rectangle = Screen.FromControl(Me).WorkingArea
                    If f.Height > wa.Height Then
                        f.Height = wa.Height - 20
                        f.Top = wa.Top + 10
                    End If
                End Sub
            f.ShowDialog(Me)
        End Using
    End Sub

    Private Sub AddMember(host As FlowLayoutPanel, name As String, role As String)
        Dim c As Color
        Dim blurb As String
        Select Case role
            Case "Backend"
                c = Color.FromArgb(37, 99, 235)
                blurb = "Data storage, security and system logic"
            Case "Project Manager"
                c = Color.FromArgb(217, 119, 6)
                blurb = "Planning, coordination and delivery"
            Case "UI Design"
                c = Color.FromArgb(219, 39, 119)
                blurb = "Screen layout, visuals and user experience"
            Case Else
                c = Color.FromArgb(22, 163, 74)
                blurb = "Testing, bug finding and quality checks"
        End Select
        host.Controls.Add(MemberCard(name, role, blurb, c))
    End Sub

    Private Shared Function Initials(name As String) As String
        Dim parts As String() = name.Split(","c)
        Dim sur As String = parts(0).Trim()
        Dim given As String = If(parts.Length > 1, parts(1).Trim(), "")
        Dim a As String = If(given <> "", given.Substring(0, 1), "")
        Dim b As String = If(sur <> "", sur.Substring(0, 1), "")
        Return (a & b).ToUpperInvariant()
    End Function

    Private Function MemberCard(name As String, role As String, blurb As String, c As Color) As Panel
        Dim cardBg As Color = Color.FromArgb(244, 248, 243)
        Dim card As New Panel With {.Size = New Size(396, 68), .Margin = New Padding(0, 0, 0, 6), .BackColor = cardBg}
        Dim ini As String = Initials(name)
        Dim av As New Panel With {.Size = New Size(44, 44), .Location = New Point(12, 12), .BackColor = cardBg}
        AddHandler av.Paint,
            Sub(sender As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(c)
                    e.Graphics.FillEllipse(br, 0, 0, av.Width - 1, av.Height - 1)
                End Using
                Using fnt As New Font("Segoe UI", 11, FontStyle.Bold)
                    Using sf As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                        e.Graphics.DrawString(ini, fnt, Brushes.White, New RectangleF(0, 0, av.Width, av.Height), sf)
                    End Using
                End Using
            End Sub
        card.Controls.Add(av)
        card.Controls.Add(New Label With {.Text = name, .AutoSize = True, .Location = New Point(66, 4),
                                          .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
                                          .ForeColor = Color.FromArgb(30, 41, 59), .BackColor = cardBg})
        card.Controls.Add(New Label With {.Text = role, .AutoSize = True, .Location = New Point(68, 27),
                                          .Padding = New Padding(7, 1, 7, 1), .ForeColor = Color.White, .BackColor = c,
                                          .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold)})
        card.Controls.Add(New Label With {.Text = blurb, .AutoSize = True, .Location = New Point(66, 48),
                                          .Font = New Font("Segoe UI", 8.5F),
                                          .ForeColor = Color.FromArgb(90, 98, 112), .BackColor = cardBg})
        Return card
    End Function

    Private Sub btnStart_Click(sender As Object, e As EventArgs) Handles btnStart.Click
        StartCam()
    End Sub

    Private Sub btnReset_Click(sender As Object, e As EventArgs) Handles btnReset.Click
        ResetScan()
    End Sub

    Private Sub btnManual_Click(sender As Object, e As EventArgs) Handles btnManual.Click
        Dim v = txtManual.Text.Trim()
        If v <> "" Then
            StopCam() : _locked = True
            HandleCode(v)
            txtManual.Clear()
        End If
    End Sub

    Private Sub LoadCams()
        cboCam.Items.Clear()
        _camMonikers.Clear()
        Try
            Dim devs As New FilterInfoCollection(FilterCategory.VideoInputDevice)
            Dim best = -1
            For i = 0 To devs.Count - 1
                cboCam.Items.Add(devs(i).Name)
                _camMonikers.Add(devs(i).MonikerString)
                Dim n = devs(i).Name.ToLowerInvariant()
                Dim skip = n.Contains("virtual") OrElse n.Contains("obs") OrElse n.Contains("snap") OrElse
                           n.Contains("droidcam") OrElse n.Contains("iriun") OrElse n.Contains("phone link") OrElse
                           n.Contains("ir camera") OrElse n.Contains(" ir ") OrElse n.Contains("infrared")
                If best = -1 AndAlso Not skip Then best = i
            Next
            If cboCam.Items.Count > 0 Then cboCam.SelectedIndex = If(best >= 0, best, 0)
        Catch
        End Try
    End Sub

    Private Sub ShowErr(msg As String)
        lblErr.Text = msg
    End Sub

    Private Sub ResetScan()
        StopCam()
        _locked = False : _step = 1 : _user = Nothing
        pnlConfirm.Visible = False : pnlScan.Visible = True
        lblErr.Text = ""
        lblStep.Text = If(_linkMode, $"Registering {_linkName}: scan their existing QR", "Step 1 of 3: scan the ID QR (faculty or OJT)")
        lblMsg.Text = "Camera is off"
        btnStart.Visible = True : btnStart.Text = "Start camera"
    End Sub

    Private Sub StartCam()
        StopCam()
        _locked = False : _gotFrame = False : lblErr.Text = ""
        Try
            If _camMonikers.Count = 0 Then LoadCams()
            If _camMonikers.Count = 0 Then
                ShowErr("No camera found. Use the manual code box.")
                Return
            End If
            Dim idx = If(cboCam.SelectedIndex >= 0, cboCam.SelectedIndex, 0)
            _cam = New VideoCaptureDevice(_camMonikers(idx))
            Dim resText = ""
            Try
                Dim caps = _cam.VideoCapabilities
                If caps IsNot Nothing AndAlso caps.Length > 0 Then
                    Dim pick = caps.Where(Function(c) c.FrameSize.Width <= 1280).
                                    OrderByDescending(Function(c) c.FrameSize.Width * c.FrameSize.Height).
                                    FirstOrDefault()
                    If pick Is Nothing Then pick = caps.OrderBy(Function(c) c.FrameSize.Width * c.FrameSize.Height).First()
                    _cam.VideoResolution = pick
                    resText = $" {pick.FrameSize.Width}x{pick.FrameSize.Height}"
                End If
            Catch
            End Try
            AddHandler _cam.NewFrame, AddressOf Cam_NewFrame
            AddHandler _cam.VideoSourceError, AddressOf Cam_Error
            _cam.Start()
            lblMsg.Text = $"Starting {cboCam.Items(idx)}{resText}…"
            btnStart.Visible = False
            camWatch.Stop() : camWatch.Start()
        Catch ex As Exception
            _cam = Nothing
            ShowErr("Camera error: " & ex.GetType().Name & ": " & ex.Message)
        End Try
    End Sub

    Private Sub Cam_Error(sender As Object, e As VideoSourceErrorEventArgs)
        Try
            BeginInvoke(Sub()
                            ShowErr("Camera error: " & e.Description)
                            btnStart.Visible = True : btnStart.Text = "Start camera"
                        End Sub)
        Catch
        End Try
    End Sub

    Private Sub StopCam()
        camWatch.Stop()
        If _cam Is Nothing Then Return
        Dim c = _cam
        _cam = Nothing
        Try
            RemoveHandler c.NewFrame, AddressOf Cam_NewFrame
            RemoveHandler c.VideoSourceError, AddressOf Cam_Error
            c.SignalToStop()
            c.WaitForStop()
        Catch
        End Try
    End Sub

    Private Sub Cam_NewFrame(sender As Object, e As NewFrameEventArgs)
        If _locked OrElse Not IsHandleCreated Then Return
        Dim bmp = DirectCast(e.Frame.Clone(), Bitmap)
        Dim txt As String = Nothing
        Try
            Dim r = _reader.Decode(bmp)
            If r IsNot Nothing Then txt = r.Text
        Catch
        End Try
        Try
            BeginInvoke(Sub() OnFrame(bmp, txt))
        Catch
            bmp.Dispose()
        End Try
    End Sub

    Private Sub OnFrame(bmp As Bitmap, txt As String)
        If _cam Is Nothing OrElse _locked Then
            bmp.Dispose()
            Return
        End If
        If Not _gotFrame Then
            _gotFrame = True
            camWatch.Stop()
            lblMsg.Text = "Point at the QR code…"
        End If
        Dim old = pic.Image
        pic.Image = bmp
        If old IsNot Nothing Then old.Dispose()
        If txt IsNot Nothing Then
            _locked = True
            StopCam()
            lblMsg.Text = "Scanned. Camera stopped."
            HandleCode(txt.Trim())
        End If
    End Sub

    Private Sub HandleCode(code As String)
        lblErr.Text = ""
        If _linkMode Then
            LinkExisting(code)
            Return
        End If
        If _step = 1 Then
            Dim u = S.Users.FirstOrDefault(Function(x) "USER:" & x.Id = code OrElse (Not String.IsNullOrEmpty(x.Code) AndAlso x.Code = code))
            If u Is Nothing Then
                ShowErr("Not a registered user ID. Register first, then scan again.")
                btnStart.Visible = True : btnStart.Text = "Scan again"
                Return
            End If
            ' Step 2 of 3: the password pops up right after the ID QR is scanned
            lblStep.Text = "Step 2 of 3: enter your password"
            Dim authErr As String = AuthenticateUser(u)
            If authErr IsNot Nothing Then
                ResetScan()
                ShowErr(authErr)
                Return
            End If
            _user = u : _step = 2
            lblStep.Text = "Step 3 of 3: scan the room key QR"
            lblMsg.Text = $"Hello, {u.Name} ({RoleName(u.Role)})"
            btnStart.Visible = True : btnStart.Text = "Scan key"
        Else
            Dim m = Regex.Match(code, "^ROOM:(.+)$")
            Dim room As Room = Nothing
            If m.Success Then room = S.Rooms.FirstOrDefault(Function(r) r.Id = m.Groups(1).Value)
            If room Is Nothing Then
                ShowErr("Not a valid room key QR.")
                btnStart.Visible = True : btnStart.Text = "Scan again"
                Return
            End If
            ConfirmPage(room, _user)
        End If
    End Sub

    ' Returns Nothing when the user is verified, otherwise the message to show.
    Private Function AuthenticateUser(u As User) As String
        If Not u.HasPassword() Then
            MessageBox.Show($"{u.Name} has no password yet. Create one now; it will be asked every time a key is borrowed.",
                            "Set password", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Dim np = PasswordDialog("Set your password", $"Create a password for {u.Name}:", True)
            If np Is Nothing Then Return "A password is required. Scan your ID again to set one."
            u.SetPassword(np)
            S.Save()
            Return Nothing
        End If
        For attempt As Integer = 1 To 3
            Dim pw = PasswordDialog("Password", $"{u.Name}, enter your password:", False)
            If pw Is Nothing Then Return "Password cancelled. Scan your ID again to continue."
            If u.CheckPassword(pw) Then Return Nothing
            If attempt < 3 Then
                MessageBox.Show($"Incorrect password. {3 - attempt} attempt(s) left.", "Password",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
        Next
        Return "Too many wrong passwords. Scan your ID again to retry."
    End Function

    Private Sub ConfirmPage(room As Room, u As User)
        pnlScan.Visible = False
        pnlConfirm.Visible = True
        ClearPanel(pnlConfirm)

        Dim mode As String, icon As String, title As String, extra As String, fg As Color
        If room.Holder Is Nothing Then
            mode = "borrow" : fg = GreenFg : icon = "🟢"
            title = $"Room {room.Id} is VACANT"
            extra = "Confirm to borrow this key."
        ElseIf room.Holder = u.Id Then
            mode = "return" : fg = RedFg : icon = "🔴"
            title = $"Room {room.Id} is OCCUPIED (by you)"
            extra = "Confirm to return the key and mark the room vacant."
        Else
            Dim h = S.Users.FirstOrDefault(Function(x) x.Id = room.Holder)
            mode = "blocked" : fg = RedFg : icon = "⛔"
            title = $"Room {room.Id} is OCCUPIED"
            extra = $"Key is with {If(h IsNot Nothing, h.Name, room.Holder)}. It cannot be borrowed."
        End If

        pnlConfirm.Controls.Add(NewLabel(icon, 32))
        pnlConfirm.Controls.Add(NewLabel(title, 16, True, fg))
        pnlConfirm.Controls.Add(NewLabel($"Name: {u.Name}" & vbCrLf & $"ID: {u.Id}" & vbCrLf &
                                         $"Type: {RoleName(u.Role)}" & vbCrLf & $"Room: {room.Id}", 11))
        pnlConfirm.Controls.Add(NewLabel(extra, 10))

        Dim row As New FlowLayoutPanel With {.AutoSize = True}
        If mode <> "blocked" Then
            Dim ok = NewButton(If(mode = "borrow", "Confirm borrow", "Confirm return"), True)
            AddHandler ok.Click,
                Sub()
                    If mode = "borrow" Then
                        room.Holder = u.Id : room.Since = DateTime.Now
                    Else
                        room.Holder = Nothing : room.Since = Nothing
                    End If
                    S.Log.Insert(0, New LogEntry With {.T = DateTime.Now, .Room = room.Id, .User = u.Name, .Id = u.Id, .Act = mode})
                    If S.Log.Count > 200 Then S.Log.RemoveRange(200, S.Log.Count - 200)
                    S.Save()

                    ClearPanel(pnlConfirm)
                    pnlConfirm.Controls.Add(NewLabel("✅", 32))
                    pnlConfirm.Controls.Add(NewLabel(If(mode = "borrow", "Key borrowed", "Key returned"), 16, True,
                                                     If(mode = "borrow", RedFg, GreenFg)))
                    pnlConfirm.Controls.Add(NewLabel($"Room {room.Id} is now " & If(mode = "borrow", "OCCUPIED.", "VACANT.")))
                    Dim done = NewButton("Done", True)
                    AddHandler done.Click, Sub() tabs.SelectedTab = tpRooms
                    pnlConfirm.Controls.Add(done)
                End Sub
            row.Controls.Add(ok)
        End If
        Dim no = NewButton(If(mode = "blocked", "Back", "Cancel"), False)
        AddHandler no.Click, Sub() ResetScan()
        row.Controls.Add(no)
        pnlConfirm.Controls.Add(row)
    End Sub

    Private Sub btnGenerate_Click(sender As Object, e As EventArgs) Handles btnGenerate.Click
        Dim fullName = txtName.Text.Trim()
        If fullName = "" Then Return
        Dim role = If(cboRole.SelectedIndex = 0, "FAC", "OJT")
        Dim pw = PasswordDialog("Set user password", $"Create a password for {fullName}:", True)
        If pw Is Nothing Then Return
        Dim id = NextId(role)
        Dim nu As New User With {.Id = id, .Name = fullName, .Role = role}
        nu.SetPassword(pw)
        S.Users.Add(nu)
        S.Save()
        txtName.Clear()
        If picOut.Image IsNot Nothing Then picOut.Image.Dispose()
        picOut.Image = MakeQr("USER:" & id, 8)
        lblOut.Text = $"{fullName}{vbCrLf}{id} · {RoleName(role)}"
        picOut.Visible = True : lblOut.Visible = True : btnSaveId.Visible = True
        DrawUsers()
    End Sub

    Private Sub btnExisting_Click(sender As Object, e As EventArgs) Handles btnExisting.Click
        Dim fullName = txtName.Text.Trim()
        If fullName = "" Then
            MessageBox.Show("Type the person's name first, then press Use existing QR.", "Use existing QR",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        _linkName = fullName
        _linkRole = If(cboRole.SelectedIndex = 0, "FAC", "OJT")
        _linkMode = True
        tabs.SelectedTab = tpScan
    End Sub

    Private Sub btnRemove_Click(sender As Object, e As EventArgs) Handles btnRemove.Click
        If lvUsers.SelectedItems.Count = 0 Then
            MessageBox.Show("Select a person in the list first.", "Remove person",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim uid = lvUsers.SelectedItems(0).Text
        Dim u = S.Users.FirstOrDefault(Function(x) x.Id = uid)
        If u Is Nothing Then Return
        Dim held = S.Rooms.Where(Function(r) r.Holder = u.Id).Select(Function(r) r.Id).ToList()
        If held.Count > 0 Then
            MessageBox.Show($"{u.Name} still has the key to room {String.Join(", ", held)}. " &
                            "Return the key first, then remove the person.", "Remove person",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If MessageBox.Show($"Remove {u.Name} ({u.Id})?" & vbCrLf & "Their ID QR will stop working.",
                           "Remove person", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        If Not RequirePassword("remove a person") Then Return
        S.Users.Remove(u)
        S.Log.Insert(0, New LogEntry With {.T = DateTime.Now, .Room = "", .User = u.Name, .Id = u.Id, .Act = "person removed"})
        If S.Log.Count > 200 Then S.Log.RemoveRange(200, S.Log.Count - 200)
        S.Save()
        DrawUsers()
    End Sub

    Private Sub btnResetPw_Click(sender As Object, e As EventArgs)
        If lvUsers.SelectedItems.Count = 0 Then
            MessageBox.Show("Select a person in the list first.", "Reset password",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim uid = lvUsers.SelectedItems(0).Text
        Dim u = S.Users.FirstOrDefault(Function(x) x.Id = uid)
        If u Is Nothing Then Return
        If Not RequirePassword("reset a user's password") Then Return
        Dim np = PasswordDialog("Reset user password", $"Enter a new password for {u.Name}:", True)
        If np Is Nothing Then Return
        u.SetPassword(np)
        S.Log.Insert(0, New LogEntry With {.T = DateTime.Now, .Room = "", .User = u.Name, .Id = u.Id, .Act = "password reset"})
        If S.Log.Count > 200 Then S.Log.RemoveRange(200, S.Log.Count - 200)
        S.Save()
        DrawUsers()
        MessageBox.Show($"Password for {u.Name} was reset.", "Reset password", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub btnSaveId_Click(sender As Object, e As EventArgs) Handles btnSaveId.Click
        If picOut.Image Is Nothing Then Return
        Using dlg As New SaveFileDialog With {.Filter = "PNG image|*.png", .FileName = "id-qr.png"}
            If dlg.ShowDialog() = DialogResult.OK Then picOut.Image.Save(dlg.FileName, Imaging.ImageFormat.Png)
        End Using
    End Sub

    Private Function NextId(role As String) As String
        Dim last = 0
        S.Counters.TryGetValue(role, last)
        Dim maxExisting = S.Users.Where(Function(x) x.Role = role).
                                  Select(Function(x) IdNum(x.Id)).DefaultIfEmpty(0).Max()
        Dim n = Math.Max(last, maxExisting) + 1
        S.Counters(role) = n
        Return role & "-" & n.ToString().PadLeft(3, "0"c)
    End Function

    Private Sub LinkExisting(code As String)
        If code.StartsWith("ROOM:", StringComparison.OrdinalIgnoreCase) Then
            ShowErr("That is a room key QR, not a person's QR.")
            btnStart.Visible = True : btnStart.Text = "Scan again"
            Return
        End If
        Dim taken = S.Users.FirstOrDefault(Function(x) "USER:" & x.Id = code OrElse (Not String.IsNullOrEmpty(x.Code) AndAlso x.Code = code))
        If taken IsNot Nothing Then
            ShowErr($"This QR is already registered to {taken.Name}.")
            btnStart.Visible = True : btnStart.Text = "Scan again"
            Return
        End If
        Dim pw = PasswordDialog("Set user password", $"Create a password for {_linkName}:", True)
        If pw Is Nothing Then
            ShowErr("Registration needs a password. Scan the QR again to retry.")
            btnStart.Visible = True : btnStart.Text = "Scan again"
            Return
        End If
        Dim id = NextId(_linkRole)
        Dim nu As New User With {.Id = id, .Name = _linkName, .Role = _linkRole, .Code = code}
        nu.SetPassword(pw)
        S.Users.Add(nu)
        S.Save()
        Dim who = _linkName
        _linkMode = False
        _linkName = ""
        MessageBox.Show($"{who} is registered as {id}. Their existing QR and password now work for scanning.", "Use existing QR",
                        MessageBoxButtons.OK, MessageBoxIcon.Information)
        txtName.Clear()
        tabsAdmin.SelectedTab = tpRegister
        tabs.SelectedTab = tpAdmin
    End Sub

    Private Shared Function IdNum(id As String) As Integer
        Dim n As Integer = 0
        Dim p = id.LastIndexOf("-"c)
        If p >= 0 Then Integer.TryParse(id.Substring(p + 1), n)
        Return n
    End Function

    Private Sub DrawUsers()
        lvUsers.Items.Clear()
        For Each u In S.Users
            lvUsers.Items.Add(New ListViewItem({u.Id, u.Name, RoleName(u.Role), If(String.IsNullOrEmpty(u.Code), "Generated", "Existing"),
                                                    If(u.HasPassword(), "Set", "Not set")}))
        Next
        UpdateAdminCounts()
    End Sub

    Private Sub btnAddRoom_Click(sender As Object, e As EventArgs) Handles btnAddRoom.Click
        Dim id = Regex.Replace(txtRoom.Text.Trim(), "[^\w\- ]", "")
        If id = "" OrElse S.Rooms.Any(Function(r) r.Id = id) Then Return
        S.Rooms.Add(New Room With {.Id = id})
        S.Save() : txtRoom.Clear() : DrawKeys()
    End Sub

    Private Sub btnRemoveRoom_Click(sender As Object, e As EventArgs) Handles btnRemoveRoom.Click
        Dim id = TryCast(cboRemoveRoom.SelectedItem, String)
        If String.IsNullOrEmpty(id) Then
            MessageBox.Show("Choose a room to remove first.", "Remove room",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim room = S.Rooms.FirstOrDefault(Function(r) r.Id = id)
        If room Is Nothing Then Return
        If S.Rooms.Count <= 1 Then
            MessageBox.Show("At least one room must remain, so the last room cannot be removed.", "Remove room",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If room.Holder IsNot Nothing Then
            Dim h = S.Users.FirstOrDefault(Function(x) x.Id = room.Holder)
            MessageBox.Show($"The key to room {id} is still borrowed by {If(h IsNot Nothing, h.Name, room.Holder)}. " &
                            "Return the key first, then remove the room.", "Remove room",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If MessageBox.Show($"Remove room {id}?" & vbCrLf & "Its key QR will stop working. Earlier log entries for this room are kept.",
                           "Remove room", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        If Not RequirePassword("remove a room") Then Return
        S.Rooms.Remove(room)
        S.Log.Insert(0, New LogEntry With {.T = DateTime.Now, .Room = id, .User = "Admin", .Id = "", .Act = "room removed"})
        If S.Log.Count > 200 Then S.Log.RemoveRange(200, S.Log.Count - 200)
        S.Save()
        DrawKeys()
    End Sub

    Private Sub btnPrint_Click(sender As Object, e As EventArgs) Handles btnPrint.Click
        PrintKeys()
    End Sub

    ' Rooms in building order: 101, 102 ... 204, 301 ... then non-numeric names (e.g. LAB-A).
    ' Used everywhere, so a room that is removed and added again returns to its original place.
    Private Function SortedRooms() As List(Of Room)
        Return S.Rooms.OrderBy(Function(x) RoomNum(x.Id)).ThenBy(Function(x) x.Id).ToList()
    End Function

    Private Sub DrawKeys()
        Dim sorted = SortedRooms()
        Dim prevSel = TryCast(cboRemoveRoom.SelectedItem, String)
        cboRemoveRoom.Items.Clear()
        For Each rm In sorted
            cboRemoveRoom.Items.Add(rm.Id)
        Next
        If cboRemoveRoom.Items.Count > 0 Then
            Dim at = If(prevSel IsNot Nothing, cboRemoveRoom.Items.IndexOf(prevSel), -1)
            cboRemoveRoom.SelectedIndex = If(at >= 0, at, 0)
        End If

        _keysW = flpKeys.ClientSize.Width
        flpKeys.SuspendLayout()
        ClearPanel(flpKeys)
        flpKeys.FlowDirection = FlowDirection.TopDown
        flpKeys.WrapContents = False

        Dim usable As Integer = Math.Max(300, flpKeys.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12)
        Dim dark As Color = Color.FromArgb(30, 41, 59)
        ' One section per floor, floor 1 first, "Other rooms" last
        Dim floors = sorted.GroupBy(Function(rm) FloorOf(rm.Id)).
                            OrderBy(Function(grp) If(grp.Key = 0, Integer.MaxValue, grp.Key))
        For Each fl In floors
            Dim head As Label = NewLabel(If(fl.Key = 0, "Other rooms", $"Floor {fl.Key}"), 11, True, Accent)
            head.Margin = New Padding(6, 10, 3, 0)
            flpKeys.Controls.Add(head)

            Dim row As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                                 .WrapContents = True, .FlowDirection = FlowDirection.LeftToRight,
                                                 .MaximumSize = New Size(usable, 0), .Margin = New Padding(0)}
            For Each r In fl
                Dim card As New Panel With {.Width = 160, .Height = 190, .Margin = New Padding(6),
                                            .BorderStyle = BorderStyle.FixedSingle, .BackColor = Color.White}
                Dim lbl As New Label With {.Dock = DockStyle.Bottom, .Height = 28, .TextAlign = ContentAlignment.MiddleCenter,
                                           .Text = "Room " & r.Id, .Font = New Font("Segoe UI", 10, FontStyle.Bold),
                                           .ForeColor = dark, .BackColor = Color.White}
                Dim pb As New PictureBox With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom,
                                               .BackColor = Color.White, .Image = MakeQr("ROOM:" & r.Id, 6)}
                card.Controls.Add(pb)
                card.Controls.Add(lbl)
                row.Controls.Add(card)
            Next
            flpKeys.Controls.Add(row)
        Next
        flpKeys.ResumeLayout()
        UpdateAdminCounts()
    End Sub

    Private Sub PrintKeys()
        Dim idx = 0
        Dim ids = SortedRooms().Select(Function(r) r.Id).ToList()
        Using pd As New PrintDocument()
            AddHandler pd.PrintPage,
                Sub(sender, e)
                    Dim g = e.Graphics
                    g.InterpolationMode = InterpolationMode.NearestNeighbor
                    g.PixelOffsetMode = PixelOffsetMode.Half
                    Dim cellW = 200, cellH = 230
                    Dim cols = Math.Max(1, e.MarginBounds.Width \ cellW)
                    Dim col = 0, y = e.MarginBounds.Top
                    e.HasMorePages = False
                    Using f As New Font("Segoe UI", 12, FontStyle.Bold)
                        While idx < ids.Count
                            If col = 0 AndAlso y + cellH > e.MarginBounds.Bottom Then
                                e.HasMorePages = True
                                Return
                            End If
                            Dim x = e.MarginBounds.Left + col * cellW
                            Using bmp = MakeQr("ROOM:" & ids(idx), 8)
                                g.DrawImage(bmp, x + 20, y, 160, 160)
                            End Using
                            g.DrawString("Room " & ids(idx), f, Brushes.Black, x + 55, y + 165)
                            col += 1
                            If col >= cols Then col = 0 : y += cellH
                            idx += 1
                        End While
                    End Using
                End Sub
            Using dlg As New PrintPreviewDialog With {.Document = pd, .Width = 900, .Height = 700}
                dlg.ShowDialog()
            End Using
        End Using
    End Sub

    Private Function PasswordDialog(title As String, prompt As String, confirm As Boolean) As String
        Using f As New Form With {.Text = title, .FormBorderStyle = FormBorderStyle.FixedDialog,
                                  .StartPosition = FormStartPosition.CenterParent,
                                  .MaximizeBox = False, .MinimizeBox = False, .ShowInTaskbar = False,
                                  .ClientSize = New Size(380, If(confirm, 190, 130))}
            Dim l As New Label With {.Text = prompt, .Left = 12, .Top = 12, .Width = 356, .Height = 22}
            Dim t1 As New TextBox With {.Left = 12, .Top = 38, .Width = 356, .UseSystemPasswordChar = True}
            Dim t2 As TextBox = Nothing
            Dim y = 72
            f.Controls.AddRange({l, t1})
            If confirm Then
                t2 = New TextBox With {.Left = 12, .Top = 72, .Width = 356, .UseSystemPasswordChar = True,
                                       .PlaceholderText = "Type it again to confirm"}
                f.Controls.Add(t2)
                y = 106
            End If
            Dim ok As New Button With {.Text = "OK", .Left = 212, .Top = y, .Width = 75, .DialogResult = DialogResult.OK}
            Dim cancel As New Button With {.Text = "Cancel", .Left = 293, .Top = y, .Width = 75, .DialogResult = DialogResult.Cancel}
            f.Controls.AddRange({ok, cancel})
            f.AcceptButton = ok : f.CancelButton = cancel
            ' Cover the whole screen with a plain white window behind the password box
            Dim cover As New Form With {.FormBorderStyle = FormBorderStyle.None, .BackColor = Color.White,
                                        .ShowInTaskbar = False, .StartPosition = FormStartPosition.Manual,
                                        .Bounds = Screen.FromControl(Me).Bounds}
            cover.Show(Me)
            Try
                Do
                    If f.ShowDialog(cover) <> DialogResult.OK Then Return Nothing
                    If t1.Text.Length = 0 Then Continue Do
                    If confirm Then
                        If t1.Text.Length < 4 Then
                            MessageBox.Show("Use at least 4 characters.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            Continue Do
                        End If
                        If t1.Text <> t2.Text Then
                            MessageBox.Show("The two passwords do not match.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            Continue Do
                        End If
                    End If
                    Return t1.Text
                Loop
            Finally
                cover.Close()
                cover.Dispose()
            End Try
        End Using
    End Function

    Private Function RequirePassword(action As String) As Boolean
        If _adminUnlocked Then Return True
        If Not S.HasPassword() Then
            Dim np = PasswordDialog("Set admin password",
                                    "No admin password is set yet. Create one to " & action & ":", True)
            If np Is Nothing Then Return False
            S.SetPassword(np) : S.Save()
            MessageBox.Show("Admin password saved. Keep it safe: it is needed to open the Admin page or clear the log.",
                            "Admin password", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return True
        End If
        Dim pw = PasswordDialog("Admin password", "Enter the admin password to " & action & ":", False)
        If pw Is Nothing Then Return False
        If S.CheckPassword(pw) Then Return True
        MessageBox.Show("Incorrect password.", "Admin password", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Return False
    End Function

    Private Sub btnChangePw_Click(sender As Object, e As EventArgs) Handles btnChangePw.Click
        If S.HasPassword() Then
            Dim cur = PasswordDialog("Change password", "Enter the current admin password:", False)
            If cur Is Nothing Then Return
            If Not S.CheckPassword(cur) Then
                MessageBox.Show("Incorrect password.", "Change password", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If
        End If
        Dim np = PasswordDialog("Change password", "Enter the new admin password:", True)
        If np Is Nothing Then Return
        S.SetPassword(np) : S.Save()
        MessageBox.Show("Admin password changed.", "Change password", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub btnClearLog_Click(sender As Object, e As EventArgs) Handles btnClearLog.Click
        If S.Log.Count = 0 Then Return
        If Not RequirePassword("clear the log") Then Return
        If MessageBox.Show("Clear all log entries? This cannot be undone.", "Clear log",
                           MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
            S.Log.Clear()
            S.Save()
            DrawLog()
        End If
    End Sub

    Private Shared Function ActText(act As String) As String
        Select Case act
            Case "borrow" : Return "Borrowed"
            Case "return" : Return "Returned"
            Case "room removed" : Return "Room removed"
            Case "person removed" : Return "Person removed"
            Case "password reset" : Return "Password reset"
            Case Else : Return act
        End Select
    End Function

    Private Sub DrawLog()
        lvLog.Items.Clear()
        For Each l In S.Log
            lvLog.Items.Add(New ListViewItem({Fmt(l.T), l.Room, l.User, ActText(l.Act)}))
        Next
    End Sub

End Class
