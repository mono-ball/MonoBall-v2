using Spectre.Console.Cli;
using Porycon3.Models;

namespace Porycon3.Commands;

public class ConvertCommand : Command<ConvertSettings>
{
    public override int Execute(CommandContext context, ConvertSettings settings)
    {
        // Use the unified executor with single Live context
        var executor = new ConvertCommandExecutor(settings);
        return executor.Execute();
    }

}
