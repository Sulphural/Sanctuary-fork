on run
	runPlugin("")
end run

on open location theURL
	runPlugin(theURL)
end open location

on runPlugin(theURL)
	set cpuKind to do shell script "uname -m"
	if cpuKind is "arm64" then
		set binaryName to "FreeRealmsPlugin-arm64"
	else
		set binaryName to "FreeRealmsPlugin-x64"
	end if
	set pluginPath to (POSIX path of (path to me)) & "Contents/Resources/" & binaryName
	if theURL is "" then
		do shell script quoted form of pluginPath & " > /dev/null 2>&1 &"
	else
		do shell script quoted form of pluginPath & " " & quoted form of theURL & " > /dev/null 2>&1 &"
	end if
end runPlugin
