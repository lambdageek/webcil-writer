# webcil-writer

This is a tool that reads a normal .NET assembly and writes it back out in a custom container format instead of the usual PE COFF.

## Building

Requires .NET 7 or later

```console
dotnet build
```

## Running

```console
dotnet run -- input.dll output.webcil
```
