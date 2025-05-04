#!/usr/bin/env bash
set -e

if [ $# -lt 2 ]; then
  echo "Usage: $0 <Configuration> <RuntimeIdentifier>"
  exit 1
fi

CONFIG=$1
RID=$2
BUILD_DIR="native/Crft.CGALWrapper/build"

# Determine Homebrew prefix
if command -v brew >/dev/null 2>&1; then
  PREFIX=$(brew --prefix)
else
  echo "Homebrew not found; please install CGAL manually."
  exit 1
fi

echo "Building Crft.CGALWrapper (config=$CONFIG)..."
mkdir -p "$BUILD_DIR"
pushd native/Crft.CGALWrapper >/dev/null
cmake -S . -B build -DCMAKE_BUILD_TYPE="$CONFIG"
cmake --build build --config "$CONFIG"
popd >/dev/null

OUT_DIR="bin/$CONFIG/net7.0/$RID/native"
mkdir -p "$OUT_DIR"
echo "Copying wrapper and CGAL libs to $OUT_DIR..."

if [[ "$(uname)" == "Darwin" ]]; then
  cp "$BUILD_DIR/libCrft.CGALWrapper.dylib" "$OUT_DIR/"
  cp "$PREFIX/lib/libCGAL"*".dylib" "$PREFIX/lib/libgmp"*".dylib" "$PREFIX/lib/libmpfr"*".dylib" "$OUT_DIR/" || true
else
  cp "$BUILD_DIR/Crft.CGALWrapper.dll" "$OUT_DIR/"
  cp "$PREFIX/bin/CGAL"*."dll" "$PREFIX/bin/gmp"*."dll" "$PREFIX/bin/mpfr"*."dll" "$OUT_DIR/" || true
fi