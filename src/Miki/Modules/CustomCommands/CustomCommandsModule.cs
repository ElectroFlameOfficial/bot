﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Miki.Attributes;
using Miki.Bot.Models;
using Miki.Cache;
using Miki.Discord;
using Miki.Discord.Common;
using Miki.Framework;
using Miki.Framework.Commands;
using Miki.Framework.Commands.Permissions.Attributes;
using Miki.Framework.Commands.Permissions.Models;
using Miki.Framework.Commands.Stages;
using Miki.Logging;
using Miki.Modules.CustomCommands.CommandHandlers;
using Miki.Modules.CustomCommands.Exceptions;
using Miki.Modules.CustomCommands.Services;
using Miki.Utility;
using MiScript;

namespace Miki.Modules.CustomCommands
{
    [Module("CustomCommands"), Emoji(AppProps.Emoji.Wrench)]
    public class CustomCommandsModule
    {
		public CustomCommandsModule(MikiApp app)
		{
            var pipeline = new CommandPipelineBuilder(app.Services)
                .UseStage(new CorePipelineStage())
                .UsePrefixes()
                .UseStage(new FetchDataStage())               
                .UseArgumentPack()
                .UseStage(new CustomCommandsHandler())
                .Build();
            app.Services.GetService<IDiscordClient>()
                .MessageCreate += async (e) => await pipeline.ExecuteAsync(e);
        }

        [Command("createcommand")]
        [GuildOnly]
        [DefaultPermission(PermissionStatus.Deny)]
        public async Task NewCustomCommandAsync(IContext e)
        {
            if(e.GetArgumentPack().Take(out string commandName))
            {
                if(commandName.Contains(' '))
                {
                    throw new InvalidCharacterException(" ");
                }

                var commandHandler = e.GetService<CommandTree>();
                if(commandHandler.GetCommand(commandName) != null)
                {
                    return;
                }

                if(!e.GetArgumentPack().CanTake)
                {
                    // TODO (Veld): Command has no function body.
                    return;
                }

                string scriptBody = e.GetArgumentPack().Pack.TakeAll().TrimStart('`').TrimEnd('`');

                try
                {
                    BlockGenerator.Compile(scriptBody);
                }
                catch(Exception ex)
                {
                    await e.ErrorEmbed($"An error occurred when parsing your script: ```{ex}```")
                        .ToEmbed().QueueAsync(e, e.GetChannel());
                    return;
                }

				try
				{
					var service = e.GetService<ICustomCommandsService>();
                    var guildId = (long)e.GetGuild().Id;

                    await service.UpdateBodyAsync(guildId, commandName, scriptBody);
                }
				catch(Exception ex)
				{
					Log.Error(ex);
				}

                await e.SuccessEmbed($"Created script '>{commandName}'")
                    .QueueAsync(e, e.GetChannel());
            }
        }

        [Command("removecommand")]
        [GuildOnly]
        [DefaultPermission(PermissionStatus.Deny)]
        public async Task RemoveCommandAsync(IContext e)
        {
            var context = e.GetService<MikiDbContext>();
            var guildId = (long)e.GetGuild().Id;

            if (e.GetArgumentPack().Take(out string commandName))
            {
                var cmd = await context.CustomCommands.FindAsync(guildId, commandName);
                if(cmd == null)
                {
                    throw new CommandNullException(commandName);
                }

                context.Remove(cmd);
                await context.SaveChangesAsync();

                await e.SuccessEmbedResource("ok_command_deleted", commandName)
                    .QueueAsync(e, e.GetChannel());
            }
            else
            {
                // No arguments provided
            }
        }
    }
}
