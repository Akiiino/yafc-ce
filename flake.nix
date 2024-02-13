{
  description = "A flake for YAFC-CE";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
  };

  outputs = { self, nixpkgs }: {
    packages.x86_64-linux.yafc-ce = nixpkgs.legacyPackages.x86_64-linux.buildDotnetModule rec {
      pname = "yafc-ce";
      version = "0.6.0";

      src = ./.;

      projectFile = "FactorioCalc.sln";
      nugetDeps = ./deps.nix;

      dotnet-sdk = nixpkgs.legacyPackages.x86_64-linux.dotnetCorePackages.sdk_6_0;
      dotnet-runtime = nixpkgs.legacyPackages.x86_64-linux.dotnetCorePackages.runtime_6_0;

      executables = [ "YAFC" ];

      runtimeDeps = [
          nixpkgs.legacyPackages.x86_64-linux.SDL2
          nixpkgs.legacyPackages.x86_64-linux.SDL2_ttf
          nixpkgs.legacyPackages.x86_64-linux.SDL2_image
      ];
      meta.mainProgram = "YAFC";
    };
  };
}

