# JanD Documentation
(up to date as of v0.4.3, lacking documentation for commands and IPC events)
## Configuration
The `JAND_PIPE` environment variable chooses the name of the IPC pipe for communication between the daemon and the CLI. By default it is `jand`.

The `JAND_HOME` environment variable is for choosing the daemon will put it's `config.json` file and the `logs` directory to.

## IPC
The IPC on Windows is available via named pipes and on Linux it's available at `/tmp/CoreFxPipe_{JAND_PIPE}`.

Messages to the daemon are sent in the following JSON format:
```json
{"Type":"packet-type","Data":"hey"}
```
### `status`
Gets daemon status.
###### Example Response
```json
{
    "Processes": 10,
    "NotSaved": true,
    "Directory": "/etc/jand"
}
```
### `exit`
Kills all processes and exits the daemon.
### `set-enabled`
Set `Enabled` of a process.
###### Data
`{ProcessName}:{boolean}` for example:  `dbus:true`
###### Response
The boolean value that was set. `True` or `False`.
### `get-process-info`
######
##### Response
 A [Proccess Info](#RuntimeProcessInfo)
 ### `stop-process`
 Kill a process.
 ###### Data
 The process' name.
 ###### Response
 `already-stopped` if not running or `killed` if killed succesfully.
### `restart-process`
###### Data
 The process' name.
###### Response
`done`
### `new-process`
Creates a new process, **enables it** but **doesn't** start it automatically.
###### Data
[New Process object](#NewProcess)
###### Response
`added` or `ERR:already-exists`
### `start-process`
Start an already existing process
###### Data
The process' name.
`ERR:already-started` or `done`
### `save-config`
###### Response
`done`
### `get-process-list`
###### Response
An array of [ProcessInfo](#ProcessInfo).
### `get-processes`
###### Response
An array of [RuntimeProcessInfo](#RuntimeProcessInfo).
### `delete-process`
Kill a process and delete it.
###### Response
`done`
### IPC Objects
#### ProcessInfo
```json
{
	"Name": "jand",
	"Command": "/usr/bin/jand start-daemon",
	"WorkingDirectory": "/etc/jand",
	"AutoRestart": true,
	"Enabled": true
}
```
#### RuntimeProcessInfo
```json
{
	"Name": "jand",
	"Command": "/usr/bin/jand start-daemon",
	"WorkingDirectory": "/home/jan/.jand",
	"ProcessId": -1,
	"Stopped": true,
	"ExitCode": 137,
	"RestartCount": 1,
	"AutoRestart": true,
	"Running": false
}
```
#### NewProcess
```json
{
	"Name": "jand",
	"Command": "/usr/bin/jand start-daemon",
	"WorkingDirectory": "/etc/jand"
}
```