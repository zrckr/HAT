#!/bin/bash
# HAT Shell Script
# Provided by FEZModding community <3
# Based on script by Ethan "flibitijibibo" Lee

# Move to script's directory
cd "`dirname "$0"`"

# Get the system architecture
UNAME=`uname`
ARCH=`uname -m`

# MonoKickstart picks the right lib* folder, so just execute the right binary.
if [ "$UNAME" == "Darwin" ]; then
	# ... Except on OSX.
	export DYLD_LIBRARY_PATH="$DYLD_LIBRARY_PATH:./osx/"
	./HAT.bin.osx $@
else
	if [ "$ARCH" == "x86_64" ]; then
		./HAT.bin.x86_64 $@
	else
		./HAT.bin.x86 $@
	fi
fi