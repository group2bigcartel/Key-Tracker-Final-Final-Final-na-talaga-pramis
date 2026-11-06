Room Key Tracker (VB.NET, Windows Forms, .NET 8)

REQUIREMENTS
- Windows 10/11
- .NET 8 SDK (https://dotnet.microsoft.com/download) or Visual Studio 2022
- A webcam (optional; a manual code box is built in)

FILES
- RoomKeyTracker.sln      Solution (open this in Visual Studio)
- RoomKeyTracker.vbproj   Project + NuGet packages (restored automatically)
- Program.vb              Data classes, JSON storage, entry point
- MainForm.vb             The whole UI and logic
- BrandAssets.vb          Embedded logos (school seal, H-E-I-M-A) as Base64 text

USER PASSWORDS
  Every user has a personal password (set when registering, 4+ characters).
  Borrowing: 1) scan the ID QR  2) enter the password  3) scan the room key QR.
  Admin > Register > "Reset password" sets a new one. Users registered before this
  update are asked to create a password the first time they scan.

ADMIN
  The Admin tab (password-protected) holds Key QRs, Register, and Log && Password (clear log, change password). The Log tab itself is public.
  The first time it is opened you create the admin password.

BUILD / RUN
  Visual Studio: open RoomKeyTracker.sln, press F5.
  Command line:  dotnet run

DATA
  Saved to %AppData%\RoomKeyTracker\data.json

NOTES
- The first build needs internet to download NuGet packages
  (QRCoder, ZXing.Net, AForge.Video.DirectShow).
- A NU1701 warning about AForge is expected and suppressed.

CREDITS
  Built by H-E-I-M-A (Group 2), 2026-2027. The "Meet the team" button (bottom-left) lists the members.
