#!/bin/bash

# Move to script's directory
cd "`dirname "$0"`"

# Provide netstandard.dll from the system Mono.
# This required at runtime by FEZRepacker but absent from FEZ's embedded Mono runtime.
NETSTANDARD=$(find /usr/lib/mono -name "netstandard.dll" -path "*/4.8*" 2>/dev/null | head -1)
[ -z "$NETSTANDARD" ] && NETSTANDARD=$(find /usr/lib/mono -name "netstandard.dll" 2>/dev/null | head -1)
[ -n "$NETSTANDARD" ] && cp "$NETSTANDARD" HATDependencies/FEZRepacker.Core/netstandard.dll

# Copy all files from HATDependencies for the duration of patching
temp_files=()
while IFS= read -r -d '' file; do
    cp "$file" . && copied_files+=("$(basename "$file")")
done < <(find HATDependencies -type f -print0)

# Patching
mono MonoMod.exe FEZ.exe

# Cleanup
for f in "${copied_files[@]}"; do
    [ "$f" = "netstandard.dll" ] && continue
    rm -f -- "$f"
done

# Copy the monokickstart binaries so that /proc/self/exe resolves to
# MONOMODDED_FEZ.bin.*, causing the embedded Mono runtime to load
# MONOMODDED_FEZ.exe instead of FEZ.exe.
[ -f FEZ.bin.x86_64 ] && cp -f FEZ.bin.x86_64 MONOMODDED_FEZ.bin.x86_64
[ -f FEZ.bin.x86 ] && cp -f FEZ.bin.x86 MONOMODDED_FEZ.bin.x86

# Create launch script mirroring the original FEZ script
cat > MONOMODDED_FEZ << 'EOF'
#!/bin/bash
# FEZ+HAT Shell Script
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
	./MONOMODDED_FEZ.bin.osx $@
else
	if [ "$ARCH" == "x86_64" ]; then
		./MONOMODDED_FEZ.bin.x86_64 $@
	else
		./MONOMODDED_FEZ.bin.x86 $@
	fi
fi
EOF

chmod +x MONOMODDED_FEZ
echo "Done! Run ./MONOMODDED_FEZ to launch the modded game."