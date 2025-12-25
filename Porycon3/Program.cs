using Spectre.Console.Cli;
using Porycon3.Commands;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("porycon3");
    config.AddCommand<ConvertCommand>("convert")
        .WithDescription("Convert pokeemerald maps to Tiled/Entity format");
});

return app.Run(args);
