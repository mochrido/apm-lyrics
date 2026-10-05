[Setup]
AppName=APM Lyrics
AppId={{2364AC50-F04A-49EF-A549-37AB82B4E4CC}
AppVersion=0.1.0
AppPublisher=APM Lyrics contributors
DefaultDirName={autopf}\APM Lyrics
DefaultGroupName=APM Lyrics
OutputDir=Output
OutputBaseFilename=APMLyricsSetup
Compression=lzma2
SolidCompression=yes
; The publish target is win-x64 and there is no 32-bit build, so the installer
; is 64-bit only.
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={app}\APMLyrics.exe

[Files]
Source: "..\src\APMLyrics\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\APM Lyrics"; Filename: "{app}\APMLyrics.exe"

[Run]
Filename: "{app}\APMLyrics.exe"; Description: "Launch APM Lyrics"; Flags: nowait postinstall skipifsilent
