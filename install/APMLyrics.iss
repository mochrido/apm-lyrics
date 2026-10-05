[Setup]
AppName=APM Lyrics
AppVersion=0.1.0
AppPublisher=APM Lyrics contributors
DefaultDirName={autopf}\APM Lyrics
DefaultGroupName=APM Lyrics
OutputDir=Output
OutputBaseFilename=APMLyricsSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={app}\APMLyrics.exe

[Files]
Source: "..\src\APMLyrics\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\APM Lyrics"; Filename: "{app}\APMLyrics.exe"
Name: "{autostartup}\APM Lyrics"; Filename: "{app}\APMLyrics.exe"; Tasks: autostart

[Tasks]
Name: "autostart"; Description: "Start APM Lyrics when Windows starts"; Flags: unchecked

[Run]
Filename: "{app}\APMLyrics.exe"; Description: "Launch APM Lyrics"; Flags: nowait postinstall skipifsilent
