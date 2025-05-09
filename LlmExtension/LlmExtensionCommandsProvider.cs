// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace LlmExtension;

public partial class LlmExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public LlmExtensionCommandsProvider()
    {
        DisplayName = "LLM Extension";
        Icon = IconHelpers.FromRelativePath("Assets\\LlmExtensionLogo.png");
        _commands = [
            new CommandItem(new LlmExtensionPage()),
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

}
