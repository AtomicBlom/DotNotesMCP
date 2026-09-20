; DotNotes installer.
;
; Compiled by build-installer.ps1 from the same staged tree the zip is made of, so the installer and
; the archive carry identical bytes and differ only in how they are laid down. ISCC is invoked with
; /DStageDir and /DAppVersion; nothing here discovers either, because a build that guesses its own
; version is a build that can disagree with the binaries it contains.
;
; One installer, both architectures. Choosing between two downloads is the step people get wrong,
; and on Windows getting it wrong does not fail -- an x64 build runs on an ARM64 machine under
; emulation and nothing says so. The [Files] Check: conditions lay down only what the machine
; executes natively, so the download is the only thing that is bigger.
;
; Requires Inno Setup 6.3 or later, for the x64os/arm64 architecture identifiers and the
; IsX64OS/IsArm64 support functions. 6.2 substitutes the deprecated x64 identifier and would install
; the x64 payload onto ARM64 -- the exact mistake the two payloads exist to prevent.

#ifndef StageDir
  #error StageDir must be passed to ISCC: /DStageDir=<path to artifacts/stage/win>
#endif

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "DotNotes"
#define AppPublisher "BinaryVibrance"
#define AppUrl "https://github.com/AtomicBlom/DotNotesMCP"
#define ServerName "dotnotes"

[Setup]
; A stable GUID is what makes the next installer recognise this one and upgrade in place rather than
; sitting beside it. It never changes, whatever the product is renamed to.
AppId={{B7E4C129-5A83-4D16-8F02-3C9A6E5B74D1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; Per-user throughout, and it needs to stay that way. The notes are the reason: a machine-wide
; install would invite a machine-wide note store, and the entire point of this store is that it
; holds what one person keeps off a shared machine and out of a repository.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\BinaryVibrance\DotNotes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; The install root is the same folder settings.json and the default note store use, so moving it
; separates an install from its own state. Offered anyway, because somebody keeping tools on another
; drive has a reason to.
DisableDirPage=no
UsePreviousAppDir=yes

; x64os rather than x64compatible: ARM64 Windows is x64-compatible through emulation, so
; x64compatible would accept an ARM64 machine and this installer would have to decide what that
; means. It carries both payloads and picks per architecture instead -- see [Files].
ArchitecturesAllowed=x64os or arm64
ArchitecturesInstallIn64BitMode=x64os or arm64

OutputDir={#StageDir}\..\..
OutputBaseFilename=dotnotes-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\bin\DotNotes.Server.exe

; Nothing here signs anything, and that is deliberate: signing happens to the finished
; dotnotes-setup.exe, after this has produced it, so whatever a pipeline signs with is that
; pipeline's business and building the installer needs no certificate at all.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Into bin/, never the root. The root's other contents are the state -- settings.json, locks/ and,
; unless this machine has pointed the store at a vault, the notes themselves. Keeping the payload
; one level down is what lets an uninstall be "delete one directory" rather than a list of things to
; spare that has to stay in step with whatever the product writes.
Source: "{#StageDir}\payload\win-x64\*"; DestDir: "{app}\bin"; Check: IsX64OS; \
    Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#StageDir}\payload\win-arm64\*"; DestDir: "{app}\bin"; Check: IsArm64; \
    Flags: recursesubdirs createallsubdirs ignoreversion

; Installed so the uninstaller can stop what it is about to remove, and extracted to {tmp} as well
; so the same thing can run before the first file is laid down.
Source: "{#StageDir}\DotNotes.Deploy.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\DotNotes.Deploy.ps1"; Flags: dontcopy
; The script form of this installer, so an install made by the wizard can still be removed by the
; same command an archive install would use.
Source: "{#StageDir}\install.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Run]
; Registration is offered, never assumed: an installer that edits somebody's agent configuration
; without being asked is doing more than installing. Through cmd so that PATH resolves claude the
; way a shell would, and hidden because the only interesting outcome is on the finish page.
Filename: "{cmd}"; \
    Parameters: "/c claude mcp add --scope user {#ServerName} -- ""{app}\bin\DotNotes.Server.exe"""; \
    Description: "Register with Claude Code (claude mcp add)"; \
    Flags: postinstall runhidden skipifsilent unchecked

[UninstallDelete]
; locks/ is transient and belongs to the product rather than to the payload, so nothing else removes
; it and it would outlive what made it.
Type: filesandordirs; Name: "{app}\locks"
; bin/ once its contents are gone: a publish leaves generated companions beside the files Inno
; recorded, and Inno removes only what it laid down.
Type: filesandordirs; Name: "{app}\bin"
; The install root itself, and ONLY if it is empty. There is deliberately no entry that removes
; {app} recursively, and none that names notes/ or settings.json. The machine store holds exactly
; what is never committed anywhere, so it is the one thing on the machine with no copy in a remote
; or a checkout -- an uninstaller that deletes it is a data-loss bug dressed as a tidy-up.
Type: dirifempty; Name: "{app}"

[Messages]
FinishedLabel=Setup has finished installing [name] on your computer.%n%nIf you did not tick the box above, register it with your agent:%n    claude mcp add --scope user dotnotes -- "<install folder>\bin\DotNotes.Server.exe"%n%nNotes already in this install were left untouched.

[Code]
const
  PowerShellQuiet = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ';

{ Runs a snippet against the shared deploy script and returns its exit code. What to stop, and what
  is safe to delete, lives in DotNotes.Deploy.ps1 because deploy.ps1 and install.ps1 need the same
  answers. Restating any of it in Pascal would be a second set of rules, and only one of them would
  get fixed -- which for the deletion rules means a version of the uninstaller that removes a store
  the other one spares. }
function RunDeployScript(const ScriptPath, Snippet: string; var ExitCode: Integer): Boolean;
var
  Command: string;
begin
  Command := PowerShellQuiet + '"' + '. ''' + ScriptPath + '''; ' + Snippet + '"';

  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Command, '',
    SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

{ Stops whatever is running out of the install and clears the old payload.

  Both matter and for different reasons. A running exe cannot be overwritten, and every editor
  session that started a server holds files in bin/ open -- so without the stop the install fails
  partway through the copy, with the install already unusable. And `publish -o` never removes what
  it does not write, so an assembly that has left the product would stay in the install forever and
  be loaded by a version that never shipped it; Inno's own file list cannot see those, because it
  never put them there.

  Clearing touches bin/ alone. Its siblings are the state, and the notes among them. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ScriptPath, Root: string;
  ExitCode: Integer;
begin
  Result := '';
  NeedsRestart := False;

  ExtractTemporaryFile('DotNotes.Deploy.ps1');
  ScriptPath := ExpandConstant('{tmp}\DotNotes.Deploy.ps1');
  Root := ExpandConstant('{app}');

  if not DirExists(Root) then
    Exit;

  if not RunDeployScript(ScriptPath,
    'Stop-Install -Root ''' + Root + ''' | Out-Null; Clear-InstallPayload -Root ''' + Root + '''',
    ExitCode) then
  begin
    Result := 'Could not run the shutdown step. Close any editor connected to DotNotes, then try again.';
    Exit;
  end;

  if ExitCode <> 0 then
    Result := 'A DotNotes server is still running and would not shut down. Close any editor connected to it, then try again.';
end;

// The uninstaller has the same problem the installer does: the thing it is removing is running. It
// runs the installed copy of the script rather than a temporary one, because an uninstall does no
// temporary extraction to have put one anywhere else.
//
// Line comments, not a braced block: in the Code section a brace opens a Pascal comment, so naming
// a constant like the app directory inside one closes it at that constant's own brace and hands the
// rest of the sentence to the compiler as code.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ScriptPath, Root: string;
  ExitCode: Integer;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  Root := ExpandConstant('{app}');
  ScriptPath := Root + '\DotNotes.Deploy.ps1';

  if FileExists(ScriptPath) then
    RunDeployScript(ScriptPath, 'Stop-Install -Root ''' + Root + ''' | Out-Null', ExitCode);
end;
