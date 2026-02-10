# develop.nix
# don't forget --pure
with import <nixpkgs> {};
pkgs.mkShell rec {

  dotnetPkg =
    (with dotnetCorePackages; combinePackages [
      sdk_10_0
    ]);

  deps = [
    zlib
    zlib.dev
    openssl
    dotnetPkg
  ];

  packages = [
    git
    godot_4
    vscodium
    ];

  NIX_LD_LIBRARY_PATH = lib.makeLibraryPath ([
    stdenv.cc.cc
  ] ++ deps);
  NIX_LD = "${pkgs.stdenv.cc.libc_bin}/bin/ld.so";
  nativeBuildInputs = [
  ] ++ deps;

  shellHook = ''
    DOTNET_ROOT="${dotnetPkg}";
    DOTNET_CLI_TELEMETRY_OPTOUT=1;
    codium .;
  '';
}
