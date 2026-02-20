# toolz.nix
{
  lib,
  buildPythonPackage,
  fetchPypi,
  setuptools,
  wheel,
  filelock,
  huggingface-hub,
  numpy,
  pyyaml,
  regex,
  requests,
  tokenizers,
  safetensors,
  tqdm
}:

buildPythonPackage
rec {
  pname = "transformers";
  version = "4.50.3";
  src = fetchPypi {
    inherit pname version;
    hash = "sha256-HXldJJJeYVqOY2h9B35Pc0jCcC64cDIobqp22DzcaE8=";
  };
  # do not run tests
  doCheck = false;

  dependencies = [
    filelock
    huggingface-hub
    numpy
    pyyaml
    regex
    requests
    tokenizers
    safetensors
    tqdm
  ];

  # specific to buildPythonPackage, see its reference
  pyproject = true;
  build-system = [
    setuptools
    wheel
  ];
}
