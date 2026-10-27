# win-task-report

Review scheduled task results, missed runs, and disabled tasks without running them.

Read-only C# / .NET 10 troubleshooting CLI for Windows 11 and Windows Server
2022/2025. No third-party runtime packages. Text and structured JSON output use
the same evidence and findings. Saved JSON can be re-analyzed offline on any
platform supported by .NET 10.

## Run

Download `win-task-report-win-x64` from a successful [GitHub Actions run](https://github.com/b69ca/win-task-report/actions),
extract it, and run `win-task-report.exe` in a terminal. The self-contained executable
includes the runtime. Initial binaries are unsigned. Build from source if preferred:

```powershell
dotnet run --project WinTaskReport.csproj -c Release -- --help
dotnet run --project WinTaskReport.csproj -c Release -- --json > report.json
dotnet run --project WinTaskReport.csproj -c Release -- --input report.json
```

## Common interface

- `--json`: complete evidence report on stdout, including collection issues and findings.
- `--input FILE`: replay this tool's schemaVersion 1 JSON report without live queries.
  Cannot be combined with live collection options. Maximum input size: 8 MiB.
- `--help`: usage and exit-code reference.

Exit codes: 0 = report produced (including warnings or partial collection);
2 = invalid usage/snapshot; 3 = collection failed; 4 = live collection requires
Windows; 130 = cancelled. Findings do not change exit status. Check JSON `status`
and `issues` before treating a report as complete. Invalid usage is written to
stderr even with `--json`; collection failures can produce a failed JSON envelope.

Snapshots include schemaVersion, tool, version, collection/analysis times,
platform, mode, status, evidence, issues, and findings. Offline analysis preserves
the collection timestamp and recomputes findings. Editing snapshots changes the
evidence; these files are not cryptographically authenticated.

## Requirements, options, and interpretation

Requires Windows PowerShell 5.1 and its built-in `ScheduledTasks` module. The .NET
executable launches the system PowerShell with no profile and a fixed embedded
script. User filters are passed as environment data, never interpolated as code.
The collector has a 90-second budget and an 8 MiB output bound.

- `--task TEXT`: literal, case-insensitive substring of task path+name, 1–256 characters.
- `--limit N`: retain the first N matching tasks sorted by path/name, 1–5000; default 1000.
- `--include-microsoft`: include tasks under `\Microsoft\`; excluded by default.

The limit bounds runtime-info queries and retained evidence; initial enumeration
still reads the local task definitions. Truncation is reported as partial evidence.
The report contains names, paths, current state, action count, last result as an
unsigned 32-bit number, last/next times in UTC, and missed-run counts. Never-run
sentinel times before 1971 become null. No action commands/arguments, task XML,
credentials, or principal account names are collected.

Known scheduler status codes such as `0x00041301` (running), `0x00041303` (has not
run), and `0x00041325` (queued) are distinguished from application exit codes.
Terminated/no-valid-trigger statuses warrant review. Unknown nonzero results are
review prompts: application documentation determines what an exit code means.
No next-run timestamp is not automatically a fault: event-driven tasks can lack
one. A previous result may coexist with a currently running task.

Disabled tasks, missed runs, and never-run tasks have separate findings. They do
not establish why a task failed to run. Review Task Scheduler history and trigger
conditions. Some tasks require elevated permissions to inspect. The tool never
requests elevation, enables history, starts a task, or changes its configuration.

```powershell
.\win-task-report.exe --task Backup
.\win-task-report.exe --include-microsoft --limit 2000 --json > tasks.json
```

Technical references: [Get-ScheduledTask](https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/get-scheduledtask),
[Get-ScheduledTaskInfo](https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/get-scheduledtaskinfo),
[Scheduler result/status constants](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-error-and-success-constants).

## Privacy and troubleshooting

No configuration changes, remediation, telemetry, or external network calls.
Reports stay on stdout unless you redirect them. Reports can reveal process,
network, or task names, addresses, and system configuration. Review and redact
before sharing. Synthetic test fixtures contain no private host data.

Run initially without elevation. Permission failures remain explicit; rerun with
appropriate privileges only if needed. No matching rule does not prove a healthy
system. Read the scope and limitations above before interpreting a finding.
Ctrl+C cancels collection where the underlying API permits it. Synchronous OS
API calls may delay cancellation. Remove the downloaded folder to uninstall.

## Build and test

Requires the .NET 10 SDK (global.json permits the latest installed 10.0 feature band).

```sh
dotnet build WinTaskReport.csproj -c Release
dotnet run --project tests/Checks.csproj -c Release
dotnet run --project WinTaskReport.csproj -c Release -- --input tests/fixture.json --json
dotnet publish WinTaskReport.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64
```

The dependency-free test executable exercises diagnostic decisions, boundaries,
malformed evidence, option validation, and CLI replay. CI runs on Ubuntu and
Windows; Windows adds live read-only collector smoke checks, PowerShell syntax
validation when applicable, standalone publishing, and executable replay. A live
smoke test establishes API compatibility, not coverage of every real-world failure.

## License

MIT — see [LICENSE](LICENSE).
