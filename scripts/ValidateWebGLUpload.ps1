$ErrorActionPreference = 'Stop'
# Read environment values as data, before cmd/ssh interpolate them into commands.
foreach ($name in @('WEB_SUB_DIR', 'APP_NAME', '_FTP_USER_')) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value) -or $value -cnotmatch '\A[A-Za-z0-9_][A-Za-z0-9_-]*\z') {
        throw "Missing or unsafe upload setting: $name"
    }
}
if ($env:_FTP_SITE_ -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9.-]*[A-Za-z0-9]\z') {
    throw 'Missing or unsafe upload host.'
}
if ($env:BUILDMODE -cnotmatch '\A(RELEASE|BETA)\z') { throw 'Invalid build mode.' }
