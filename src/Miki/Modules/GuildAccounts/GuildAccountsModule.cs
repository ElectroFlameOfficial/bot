﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Miki.Accounts;
using Miki.Attributes;
using Miki.Bot.Models;
using Miki.Discord;
using Miki.Framework;
using Miki.Framework.Commands;
using Miki.Localization;
using Miki.Services;
using Miki.Services.Transactions;
using Miki.Utility;

namespace Miki.Modules.GuildAccounts
{
    [Module("Guild_Accounts"), Emoji(AppProps.Emoji.Ledger)]
	public class GuildAccountsModule
	{
        [GuildOnly, Command("guildweekly", "weekly")]
        public async Task GuildWeeklyAsync(IContext e)
        {
            var guildService = e.GetService<IGuildService>();
            var locale = e.GetLocale();

            var response = await guildService.ClaimWeeklyAsync(
                new GuildUserReference((long)e.GetGuild().Id, (long)e.GetAuthor().Id));

            var embed = new EmbedBuilder()
                .SetTitle($"{AppProps.Emoji.WeeklyEmbedIcon} " + locale.GetString("guildweekly_title"))
                .SetColor(255, 232, 182);

            switch (response.Status)
            {
                case WeeklyStatus.Success:
                    embed.AddInlineField(
                        locale.GetString("guildweekly_success_title"),
                        locale.GetString("guildweekly_success_content", 
                            response.AmountClaimed, e.GetGuild().Name));
                    break;
                case WeeklyStatus.GuildInsufficientExp:
                    embed.AddInlineField(
                            locale.GetString("guildweekly_guildinsufficientexp_title"),
                            locale.GetString("guildweekly_guildinsufficientexp_content", e.GetGuild().Name))
                        .SetFooter(locale.GetString("guildweekly_guildinsufficientexp_footer"));
                    break;
                case WeeklyStatus.UserInsufficientExp:
                    embed.AddInlineField(
                            locale.GetString("guildweekly_userinsufficientexp_title"),
                            locale.GetString("guildweekly_userinsufficientexp_content",
                                response.ExperienceNeeded, e.GetGuild().Name))
                        .SetFooter(locale.GetString("guildweekly_userinsufficientexp_footer"));
                    break;
                case WeeklyStatus.NotReady:
                    embed.AddField(
                        locale.GetString("guildweekly_notready_title"),
                        locale.GetString("guildweekly_notready_content", e.GetGuild().Name));
                    embed.AddInlineField(
                        locale.GetString("guildweekly_notready_timeleft_title"),
                        $"`{(response.LastClaimTime.AddDays(7) - DateTime.Now).ToTimeString(e.GetLocale())}`");
                    break;
            }

            await embed.ToEmbed().QueueAsync(e, e.GetChannel());
        }

        [GuildOnly, Command("guildnewrival")]
        public async Task GuildNewRival(IContext e)
        {
            var context = e.GetService<MikiDbContext>();
            var locale = e.GetLocale();

            GuildUser thisGuild = await context.GuildUsers
                .FindAsync(e.GetGuild().Id.ToDbLong());

            if (thisGuild == null)
            {
                await e.ErrorEmbed(locale.GetString("guild_error_null"))
                    .ToEmbed()
                    .QueueAsync(e, e.GetChannel());
                return;
            }

            if (thisGuild.UserCount == 0)
            {
                thisGuild.UserCount = e.GetGuild().MemberCount;
            }

            if (thisGuild.LastRivalRenewed.AddDays(1) > DateTime.Now)
            {
                await new EmbedBuilder()
                    .SetTitle(locale.GetString("miki_terms_rival"))
                    .SetDescription(locale.GetString("guildnewrival_error_timer_running"))
                    .ToEmbed().QueueAsync(e, e.GetChannel());
                return;
            }

            List<GuildUser> rivalGuilds = await context.GuildUsers
            // TODO: refactor and potentially move into function
                .Where((g) => Math.Abs(g.UserCount - e.GetGuild().MemberCount) < g.UserCount * 0.25
                              && g.RivalId == 0 && g.Id != thisGuild.Id)
                .ToListAsync();

            if (!rivalGuilds.Any())
            {
                await e.ErrorEmbed(locale.GetString("guildnewrival_error_matchmaking_failed"))
                    .ToEmbed()
                    .QueueAsync(e, e.GetChannel());
                return;
            }

            int random = MikiRandom.Next(0, rivalGuilds.Count);

            GuildUser rivalGuild = await context.GuildUsers.FindAsync(rivalGuilds[random].Id);

            thisGuild.RivalId = rivalGuild.Id;
            rivalGuild.RivalId = thisGuild.Id;

            thisGuild.LastRivalRenewed = DateTime.Now;

            await context.SaveChangesAsync();

            await new EmbedBuilder()
                .SetTitle(locale.GetString("miki_terms_rival"))
                .SetDescription(locale.GetString(
                    "guildnewrival_success",
                    rivalGuild.Name))
                .ToEmbed()
                .QueueAsync(e, e.GetChannel());
        }

        [Command("guildbank")]
        [GuildOnly]
        public class GuildbankCommand
        {
            [Command]
            public async Task GuildBankInfoAsync(IContext e)
            {
                var locale = e.GetLocale();
                await new EmbedBuilder()
                    .SetTitle(locale.GetString("guildbank_title", e.GetGuild().Name))
                    .SetDescription(locale.GetString("guildbank_info_description"))
                    .SetColor(255, 255, 255)
                    .SetThumbnail("https://imgur.com/KXtwIWs.png")
                    .AddInlineField(
                        locale.GetString("guildbank_info_help"),
                        locale.GetString("guildbank_info_help_description", e.GetPrefixMatch()))
                    .ToEmbed()
                    .QueueAsync(e, e.GetChannel());
            }

            [Command("balance", "bal")]
            public async Task GuildBankBalanceAsync(IContext e)
            {
                var context = e.GetService<DbContext>();
                var accountService = e.GetService<IBankAccountService>();
                var locale = e.GetLocale();

                var guildUser = await context.Set<GuildUser>()
                    .SingleOrDefaultAsync(x => x.Id == (long)e.GetGuild().Id);

                var account = await accountService.GetOrCreateBankAccountAsync(
                    new AccountReference((long)e.GetAuthor().Id, (long)e.GetGuild().Id));

                await new EmbedBuilder()
                    .SetTitle(locale.GetString("guildbank_title", e.GetGuild().Name))
                    .SetThumbnail("https://imgur.com/KXtwIWs.png")
                    .SetColor(255, 255, 255)
                    .AddField(
                        locale.GetString("guildbank_balance_title"),
                        locale.GetString("guildbank_balance", $"{guildUser.Currency:N0}"))
                    .AddField(
                        locale.GetString("guildbank_contributed"),
                        $"{account.TotalDeposited:N0}")
                    .ToEmbed().QueueAsync(e, e.GetChannel());
            }

            [Command("deposit", "dep")]
            public async Task GuildBankDepositAsync(IContext e)
            {
                var context = e.GetService<DbContext>();
                var userService = e.GetService<IUserService>();
                var accountService = e.GetService<IBankAccountService>();
                var transactionService = e.GetService<ITransactionService>();
                var locale = e.GetLocale();

                var totalDeposited = e.GetArgumentPack().TakeRequired<int>();
                var user = await userService.GetOrCreateUserAsync(e.GetAuthor());

                await transactionService.CreateTransactionAsync(
                    new TransactionRequest.Builder()
                        .WithAmount(totalDeposited)
                        .WithReceiver(AppProps.Currency.BankId)
                        .WithSender(user.Id)
                        .Build());

                //TODO: Create GuildUserService.
                var guildUser = await context.Set<GuildUser>()
                    .SingleOrDefaultAsync(x => x.Id == (long)e.GetGuild().Id);
                guildUser.Currency += totalDeposited;
                context.Update(guildUser);

                var accountDetails = new AccountReference(
                    (long)e.GetAuthor().Id, (long)e.GetGuild().Id);

                await accountService.DepositAsync(accountDetails, totalDeposited);

                await new EmbedBuilder()
                    .SetAuthor("Guild bank", "https://imgur.com/KXtwIWs.png")
                    .SetDescription(locale.GetString("guildbank_deposit_title", e.GetAuthor().Username, $"{totalDeposited:N0}"))
                    .SetColor(255, 255, 255)
                    .ToEmbed()
                    .QueueAsync(e, e.GetChannel());
            }
        }

        [GuildOnly, Command("guildprofile")]
		public async Task GuildProfile(IContext e)
		{
            var context = e.GetService<MikiDbContext>();

            var locale = e.GetLocale();
            GuildUser g = await context.GuildUsers.FindAsync(e.GetGuild().Id.ToDbLong());

				int rank = await g.GetGlobalRankAsync(context);
				int level = g.CalculateLevel(g.Experience);

				EmojiBarSet onBarSet = new EmojiBarSet("<:mbarlefton:391971424442646534>", "<:mbarmidon:391971424920797185>", "<:mbarrighton:391971424488783875>");
				EmojiBarSet offBarSet = new EmojiBarSet("<:mbarleftoff:391971424824459265>", "<:mbarmidoff:391971424824197123>", "<:mbarrightoff:391971424862208000>");

				EmojiBar expBar = new EmojiBar(g.CalculateMaxExperience(g.Experience), onBarSet, offBarSet, 6);

				EmbedBuilder embed = new EmbedBuilder()
					.SetAuthor(g.Name, e.GetGuild().IconUrl, "https://miki.veld.one")
					.SetColor(0.1f, 0.6f, 1)
					.AddInlineField(locale.GetString("miki_terms_level"), level.ToString("N0"));

				if((e.GetGuild().IconUrl ?? "") != "")
				{
					embed.SetThumbnail("http://veld.one/assets/img/transparentfuckingimage.png");
				}

				string expBarString = expBar.Print(g.Experience);

                embed.AddInlineField(e.GetLocale().GetString("miki_terms_experience"),
                    $"[{g.Experience:N0} / {g.CalculateMaxExperience(g.Experience):N0}]\n" 
                    + (expBarString ?? ""));

				embed.AddInlineField(
					e.GetLocale().GetString("miki_terms_rank"), 
					"#" + (rank <= 10 ? $"**{rank:N0}**" : rank.ToString("N0"))
				).AddInlineField(
					e.GetLocale().GetString("miki_module_general_guildinfo_users"),
					g.UserCount.ToString()
				);

				GuildUser rival = await g.GetRivalOrDefaultAsync(context);

				if(rival != null)
				{
					embed.AddInlineField(e.GetLocale().GetString("miki_terms_rival"), $"{rival.Name} [{rival.Experience:N0}]");
				}

                await embed.ToEmbed()
                    .QueueAsync(e, e.GetChannel());
		}

        [GuildOnly, Command("guildconfig")]
        public async Task SetGuildConfig(IContext e)
        {
            var context = e.GetService<MikiDbContext>();

            GuildUser g = await context.GuildUsers.FindAsync(e.GetGuild().Id.ToDbLong());

            if(e.GetArgumentPack().Take(out string arg))
            {
                switch(arg)
                {
                    case "expneeded":
                    {
                        if(e.GetArgumentPack().Take(out int value))
                        {
                            g.MinimalExperienceToGetRewards = value;

                            await new EmbedBuilder()
                                .SetTitle(e.GetLocale().GetString("miki_terms_config"))
                                .SetDescription(e.GetLocale().GetString("guildconfig_expneeded",
                                    value.ToString("N0")))
                                .ToEmbed()
                                .QueueAsync(e, e.GetChannel());
                        }
                    }
                        break;

                    case "visible":
                    {
                        if(e.GetArgumentPack().Take(out bool value))
                        {
                            string resourceString = value
                                ? "guildconfig_visibility_true"
                                : "guildconfig_visibility_false";

                            await new EmbedBuilder()
                                .SetTitle(e.GetLocale().GetString("miki_terms_config"))
                                .SetDescription(resourceString)
                                .ToEmbed().QueueAsync(e, e.GetChannel());
                        }
                    }
                        break;
                }

                await context.SaveChangesAsync();
            }
            else
            {
                await new EmbedBuilder()
                {
                    Title = e.GetLocale().GetString("guild_settings"),
                    Description = e.GetLocale().GetString("miki_command_description_guildconfig")
                }.ToEmbed().QueueAsync(e, e.GetChannel());
            }
        }

        [GuildOnly, Command("guildupgrade")]
		public async Task GuildUpgradeAsync(IContext e)
		{
            e.GetArgumentPack().Take(out string arg);
            var context = e.GetService<MikiDbContext>();

            var guildUser = await context.GuildUsers
					.FindAsync(e.GetGuild().Id.ToDbLong());

				switch (arg)
				{
					case "house":
					{
						guildUser.RemoveCurrency(guildUser.GuildHouseUpgradePrice);
						guildUser.GuildHouseLevel++;

						await context.SaveChangesAsync();

                        await e.SuccessEmbed("Upgraded your guild house!")
							.QueueAsync(e, e.GetChannel());
					} break;

					default:
					{
                        await new EmbedBuilder()
							.SetTitle("Guild Upgrades")
							.SetDescription("Guild upgrades are a list of things you can upgrade for your guild to get more rewards! To purchase one of the upgrades, use `>guildupgrade <upgrade name>` an example would be `>guildupgrade house`")
							.AddField("Upgrades",
								$"`house` - Upgrades weekly rewards (costs: {guildUser.GuildHouseUpgradePrice:N0})")
							.ToEmbed().QueueAsync(e, e.GetChannel());
					} break;
				}
		}

		[GuildOnly, Command("guildhouse")]
		public async Task GuildHouseAsync(IContext e)
		{
            var context = e.GetService<MikiDbContext>();

            var guildUser = await context.GuildUsers
					.FindAsync(e.GetGuild().Id.ToDbLong());

                await new EmbedBuilder()
					.SetTitle("🏠 Guild house")
					.SetColor(255, 232, 182)
					.SetDescription(e.GetLocale().GetString("guildhouse_buy", guildUser.GuildHouseUpgradePrice.ToString("N0")))
					.AddInlineField("Current weekly bonus", $"x{guildUser.GuildHouseMultiplier}")
					.AddInlineField("Current house level", e.GetLocale().GetString($"guildhouse_rank_{guildUser.GuildHouseLevel}"))
					.ToEmbed().QueueAsync(e, e.GetChannel());
		}
	}
}
