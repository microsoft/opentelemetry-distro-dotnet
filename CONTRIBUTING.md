# Contributing

This project welcomes contributions and suggestions. Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant Microsoft the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide a CLA and decorate the pull request appropriately. Follow the instructions provided by the bot. You only need to do this once across all repositories using Microsoft's CLA process.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with questions or concerns.

## Before You Start

- Search existing issues before opening a new one.
- Open an issue before starting large changes so the scope and direction can be discussed.
- Keep changes focused and include tests when behavior changes.

## Development Setup

## Build

We depend on the [.NET CLI](https://docs.microsoft.com/dotnet/core/tools/) to build these projects/solutions.
To successfully build the sources on your machine, make sure you've installed the following prerequisites:
* Visual Studio 2022+ or Visual Studio Code
* [.NET SDK (latest stable version)](https://dotnet.microsoft.com/download)

Solutions can be built in either Visual Studio or via .NET CLI `dotnet build` ([link](https://docs.microsoft.com/dotnet/core/tools/dotnet-build)).

Unit tests can be run in either the Visual Studio Test Explorer or via .NET CLI `dotnet test` ([link](https://docs.microsoft.com/dotnet/core/tools/dotnet-test)).

## Pull Requests

- Describe the problem and the approach clearly.
- Link related issues when applicable.
- Update documentation when public behavior or setup changes.
- Keep the repository planning and README documents aligned with the implementation.
