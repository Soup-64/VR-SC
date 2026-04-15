# develop.nix
# don't forget --pure
with import <nixpkgs> {};
pkgs.mkShell rec {

  dotnetPkg =
    (with dotnetCorePackages; combinePackages [
      sdk_10_0
      sdk_8_0
      sdk_9_0
    ]);

  deps = [
    pkg-config
    zlib
    zlib.dev
    openssl
    dotnetPkg
    #   just send it honestly
    gst_all_1.gst-devtools
    gst_all_1.gst-libav
    gst_all_1.gstreamer
    gst_all_1.gst-vaapi
    gst_all_1.gst-plugins-bad
    gst_all_1.gst-plugins-ugly
    gst_all_1.gst-plugins-good
    gst_all_1.gst-plugins-base
    gst_all_1.gst-plugins-rs #more av1 and other improved plugins
  ];

  packages = [
    git
    godot-mono.unwrapped
    vscodium
    dotnetPkg
    xxd #lowkirkenuinely forgot what this is
  ];

# required to have gst sharp load the dlls properly
  LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath (with pkgs; [
    gst_all_1.gst-devtools
    gst_all_1.gst-libav
    gst_all_1.gstreamer
    gst_all_1.gst-vaapi
    gst_all_1.gst-plugins-bad
    gst_all_1.gst-plugins-ugly
    gst_all_1.gst-plugins-good
    gst_all_1.gst-plugins-base
    pipewire
  ]);

  GST_PLUGIN_SYSTEM_PATH_1_0 = pkgs.lib.makeSearchPathOutput "lib" "lib/gstreamer-1.0" (with pkgs; [
    gst_all_1.gst-devtools
    gst_all_1.gst-libav
    gst_all_1.gstreamer
    gst_all_1.gst-vaapi
    gst_all_1.gst-plugins-bad
    gst_all_1.gst-plugins-ugly
    gst_all_1.gst-plugins-good
    gst_all_1.gst-plugins-base
    pipewire
  ]);

  NIX_LD_LIBRARY_PATH = lib.makeLibraryPath ([
    stdenv.cc.cc
  ] ++ deps);
  NIX_LD = "${pkgs.stdenv.cc.libc_bin}/bin/ld.so";
  nativeBuildInputs = [
  ] ++ deps;



  shellHook = ''
    ln -sf $(which godot-mono) ./godot-mono
    DOTNET_ROOT="${dotnetPkg}";
    DOTNET_CLI_TELEMETRY_OPTOUT=1;
  '';
}
