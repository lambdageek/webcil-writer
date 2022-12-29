using System;
using Microsoft.Extensions.Logging;

namespace WebcilWriter;

public class WebcilWriter
{
    public static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2)
        {
            Console.WriteLine("Usage: WebcilWriter <inputPath> [<outputPath>]");
            return 1;
        }
        var inputPath = args[0];
        var outputPath = args.Length > 1 ? args[1] : Path.ChangeExtension(inputPath, ".webcil");
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;

            });
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var logger = loggerFactory.CreateLogger<WebcilWriter>();
        var w = new Microsoft.WebAssembly.Metadata.WebcilWriter(inputPath, outputPath, logger);
        w.Write();
        return 0;
    }
}