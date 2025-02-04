using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Builders;

public class UserBuilder
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly TimerService _timer;
    private readonly FeaturedService _featuredService;
    private readonly BotSettings _botSettings;
    private readonly LastFmRepository _lastFmRepository;
    private readonly PlayService _playService;
    private readonly TimeService _timeService;
    private readonly ArtistsService _artistsService;
    private readonly SupporterService _supporterService;
    private readonly DiscogsService _discogsService;

    public UserBuilder(UserService userService,
        GuildService guildService,
        IPrefixService prefixService,
        TimerService timer,
        IOptions<BotSettings> botSettings,
        FeaturedService featuredService,
        LastFmRepository lastFmRepository,
        PlayService playService,
        TimeService timeService,
        ArtistsService artistsService,
        SupporterService supporterService,
        DiscogsService discogsService)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._timer = timer;
        this._featuredService = featuredService;
        this._lastFmRepository = lastFmRepository;
        this._playService = playService;
        this._timeService = timeService;
        this._artistsService = artistsService;
        this._supporterService = supporterService;
        this._discogsService = discogsService;
        this._botSettings = botSettings.Value;
    }

    public async Task<ResponseModel> FeaturedAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var guild = await this._guildService.GetGuildWithGuildUsers(context.DiscordGuild?.Id);

        if (this._timer._currentFeatured == null)
        {
            response.ResponseType = ResponseType.Text;
            response.Text = ".fmbot is still starting up, please try again in a bit..";
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        response.Embed.WithThumbnailUrl(this._timer._currentFeatured.ImageUrl);
        response.Embed.AddField("Featured:", this._timer._currentFeatured.Description);

        if (guild?.GuildUsers != null && guild.GuildUsers.Any() && this._timer._currentFeatured.UserId.HasValue && this._timer._currentFeatured.UserId.Value != 0)
        {
            var guildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == this._timer._currentFeatured.UserId);

            if (guildUser != null)
            {
                response.Text = "in-server";
                response.Embed.AddField("🥳 Congratulations!", $"This user is in your server under the name {guildUser.UserName}.");
            }
        }

        if (this._timer._currentFeatured.SupporterDay)
        {
            var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
            if (randomHintNumber == 1 && this._supporterService.ShowPromotionalMessage(context.ContextUser.UserType, context.DiscordGuild?.Id))
            {
                this._supporterService.SetGuildPromoCache(context.DiscordGuild?.Id);
                response.Embed.AddField("Get featured", $"*Also want a higher chance of getting featured on Supporter Sunday? " +
                                                            $"[View all perks and get .fmbot supporter here.]({Constants.GetSupporterOverviewLink})*");
            }
        }

        response.Embed.WithFooter($"View your featured history with '{context.Prefix}featuredlog'");

        if (PublicProperties.IssuesAtLastFm)
        {
            response.Embed.AddField("Note:", "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues");
        }

        return response;
    }

    public async Task<ResponseModel> BotScrobblingAsync(ContextModel context, string option)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var newBotScrobblingDisabledSetting = await this._userService.ToggleBotScrobblingAsync(context.ContextUser, option);

        response.Embed.WithDescription("Bot scrobbling allows you to automatically scrobble music from Discord music bots to your Last.fm account. " +
                                    "For this to work properly you need to make sure .fmbot can see the voice channel and use a supported music bot.\n\n" +
                                    "Only tracks that already exist on Last.fm will be scrobbled. This feature works best with Spotify music.\n\n" +
                                    "Currently supported bots:\n" +
                                    "- Hydra (Only with Now Playing messages enabled in English)\n" +
                                    "- Cakey Bot (Only with Now Playing messages enabled in English)\n" +
                                    "- SoundCloud");

        if ((newBotScrobblingDisabledSetting == null || newBotScrobblingDisabledSetting == false) && !string.IsNullOrWhiteSpace(context.ContextUser.SessionKeyLastFm))
        {
            response.Embed.AddField("Status", "✅ Enabled and ready.");
            response.Embed.WithFooter($"Use '{context.Prefix}botscrobbling off' to disable.");
        }
        else if ((newBotScrobblingDisabledSetting == null || newBotScrobblingDisabledSetting == false) && string.IsNullOrWhiteSpace(context.ContextUser.SessionKeyLastFm))
        {
            response.Embed.AddField("Status", $"⚠️ Bot scrobbling is enabled, but you need to login through `{context.Prefix}login` first.");
        }
        else
        {
            response.Embed.AddField("Status", $"❌ Disabled. Do '{context.Prefix}botscrobbling on' to enable.");
        }

        return response;
    }

    public static ResponseModel Mode(
        ContextModel context,
        Guild guild = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var fmType = new SelectMenuBuilder()
                .WithPlaceholder("Select embed type")
                .WithCustomId(InteractionConstants.FmSettingType)
                .WithMinValues(1)
                .WithMaxValues(1);

        foreach (var name in Enum.GetNames(typeof(FmEmbedType)).OrderBy(o => o))
        {
            fmType.AddOption(new SelectMenuOptionBuilder(name, name));
        }

        var maxOptions = context.ContextUser.UserType == UserType.User
            ? Constants.MaxFooterOptions
            : Constants.MaxFooterOptionsSupporter;

        var fmOptions = new SelectMenuBuilder()
            .WithPlaceholder("Select footer options")
            .WithCustomId(InteractionConstants.FmSettingFooter)
            .WithMaxValues(maxOptions);

        var fmSupporterOptions = new SelectMenuBuilder()
            .WithPlaceholder("Select supporter-exclusive footer option")
            .WithCustomId(InteractionConstants.FmSettingFooterSupporter)
            .WithMinValues(0)
            .WithMaxValues(1);

        foreach (var option in ((FmFooterOption[])Enum.GetValues(typeof(FmFooterOption))).Where(w => w != FmFooterOption.None))
        {
            var name = option.GetAttribute<OptionAttribute>().Name;
            var description = option.GetAttribute<OptionAttribute>().Description;
            var supporterOnly = option.GetAttribute<OptionAttribute>().SupporterOnly;
            var value = Enum.GetName(option);

            var active = context.ContextUser.FmFooterOptions.HasFlag(option);

            if (!supporterOnly)
            {
                fmOptions.AddOption(new SelectMenuOptionBuilder(name, value, description, isDefault: active));
            }
            else
            {
                fmSupporterOptions.AddOption(new SelectMenuOptionBuilder(name, value, description, isDefault: active));
            }
        }

        var builder = new ComponentBuilder()
            .WithSelectMenu(fmType)
            .WithSelectMenu(fmOptions, 1);

        if (context.ContextUser.UserType != UserType.User)
        {
            builder.WithSelectMenu(fmSupporterOptions, 2);
        }

        response.Components = builder;

        response.Embed.WithAuthor("Configuring your 'fm' command");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var embedDescription = new StringBuilder();

        embedDescription.AppendLine("Use the dropdowns below to configure how your `fm` command looks.");
        embedDescription.AppendLine();


        embedDescription.Append($"The first dropdown allows you to select a mode, while the second allows you to select up to {maxOptions} options that will be displayed in the footer. ");
        if (context.ContextUser.UserType != UserType.User)
        {
            embedDescription.Append($"The third dropdown lets you select 1 supporter-exclusive option.");
        }

        embedDescription.AppendLine();

        embedDescription.AppendLine();
        embedDescription.Append($"Some options might not always show up on every track, for example when no source data is available. ");

        if (context.ContextUser.UserType == UserType.User)
        {
            embedDescription.Append($"[.fmbot supporters]({Constants.GetSupporterOverviewLink}) can select up to {Constants.MaxFooterOptionsSupporter} options.");
        }

        if (guild?.FmEmbedType != null)
        {
            embedDescription.AppendLine();
            embedDescription.AppendLine(
                $"Note that servers can force a specific mode which will override your own mode. ");
            embedDescription.AppendLine(
                $"This server has the **{guild.FmEmbedType}** mode set for everyone, which means your own setting will not apply here.");
        }

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }

    public async Task<ResponseModel> FeaturedLogAsync(ContextModel context, UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithTitle(
                    $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}'s featured history");

        var featuredHistory = await this._featuredService.GetFeaturedHistoryForUser(userSettings.UserId);

        var description = new StringBuilder();
        var odds = await this._featuredService.GetFeaturedOddsAsync();
        var nextSupporterSunday = FeaturedService.GetDaysUntilNextSupporterSunday();

        if (!featuredHistory.Any())
        {
            if (!userSettings.DifferentUser)
            {
                description.AppendLine("Sorry, you haven't been featured yet... <:404:882220605783560222>");
                description.AppendLine();
                description.AppendLine($"But don't give up hope just yet!");
                description.AppendLine($"Every hour there is a 1 in {odds} chance that you might be picked.");

                if (context.DiscordGuild?.Id != this._botSettings.Bot.BaseServerId)
                {
                    description.AppendLine();
                    description.AppendLine($"Join [our server](https://discord.gg/6y3jJjtDqK) to get pinged if you get featured.");
                }

                if (context.ContextUser.UserType == UserType.Supporter)
                {
                    description.AppendLine();
                    description.AppendLine($"Also, as a thank you for being a supporter you have a higher chance of becoming featured every first Sunday of the month on Supporter Sunday. The next one is in {nextSupporterSunday} {StringExtensions.GetDaysString(nextSupporterSunday)}.");
                }
                else
                {
                    description.AppendLine();
                    description.AppendLine($"Become an [.fmbot supporter](https://opencollective.com/fmbot/contribute) and get a higher chance every Supporter Sunday. The next Supporter Sunday is in {nextSupporterSunday} {StringExtensions.GetDaysString(nextSupporterSunday)} (first Sunday of each month).");
                }
            }
            else
            {
                description.AppendLine("Hmm, they haven't been featured yet... <:404:882220605783560222>");
                description.AppendLine();
                description.AppendLine($"But don't let them give up hope just yet!");
                description.AppendLine($"Every hour there is a 1 in {odds} chance that they might be picked.");
            }
        }
        else
        {
            foreach (var featured in featuredHistory.Take(12))
            {
                var dateValue = ((DateTimeOffset)featured.DateTime).ToUnixTimeSeconds();
                description.AppendLine($"Mode: `{featured.FeaturedMode}`");
                if (featured.TrackName != null)
                {
                    description.AppendLine($"**{featured.TrackName}**");
                    description.AppendLine($"**{featured.ArtistName}** | *{featured.AlbumName}*");
                }
                else
                {
                    description.AppendLine($"**{featured.ArtistName}** - **{featured.AlbumName}**");
                }

                description.AppendLine($"<t:{dateValue}:F> (<t:{dateValue}:R>)");

                if (featured.SupporterDay)
                {
                    description.AppendLine($"⭐ On supporter Sunday");
                }

                description.AppendLine();
            }

            var self = userSettings.DifferentUser ? "They" : "You";
            var footer = new StringBuilder();

            footer.AppendLine(featuredHistory.Count == 1
                ? $"{self} have only been featured once. Every hour, that is a chance of 1 in {odds}!"
                : $"{self} have been featured {featuredHistory.Count} times");

            if (context.ContextUser.UserType == UserType.Supporter)
            {
                footer.AppendLine($"As a thank you for supporting, you have better odds every first Sunday of the month.");
            }
            else
            {
                footer.AppendLine($"Every first Sunday of the month is Supporter Sunday (in {nextSupporterSunday} {StringExtensions.GetDaysString(nextSupporterSunday)}). Check '{context.Prefix}getsupporter' for info.");
            }

            response.Embed.WithFooter(footer.ToString());
        }

        response.Embed.WithDescription(description.ToString());

        return response;
    }

    public async Task<ResponseModel> StatsAsync(ContextModel context, UserSettingsModel userSettings, User user)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        string userTitle;
        if (userSettings.DifferentUser)
        {
            if (userSettings.DifferentUser && user.DiscordUserId == userSettings.DiscordUserId)
            {
                response.Embed.WithDescription("That user is not registered in .fmbot.");
                response.CommandResponse = CommandResponse.WrongInput;
                return response;
            }

            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
            user = await this._userService.GetFullUserAsync(userSettings.DiscordUserId);
        }
        else
        {
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }

        response.EmbedAuthor.WithName($"Stats for {userTitle}");
        response.EmbedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}");
        response.Embed.WithAuthor(response.EmbedAuthor);

        var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(userSettings.UserNameLastFm);

        var userAvatar = userInfo.Image?.FirstOrDefault(f => f.Size == "extralarge");
        if (!string.IsNullOrWhiteSpace(userAvatar?.Text))
        {
            response.Embed.WithThumbnailUrl(userAvatar.Text);
        }

        var description = new StringBuilder();
        if (user.UserType != UserType.User)
        {
            description.AppendLine($"{userSettings.UserType.UserTypeToIcon()} .fmbot {userSettings.UserType.ToString().ToLower()}");
        }

        if (this._supporterService.ShowPromotionalMessage(context.ContextUser.UserType, context.DiscordGuild?.Id))
        {
            var random = new Random().Next(0, Constants.SupporterPromoChance);
            if (random == 1)
            {
                this._supporterService.SetGuildPromoCache(context.DiscordGuild?.Id);
                description.AppendLine($"*Want to see an overview of all your years? [View all perks and get .fmbot supporter here.]({Constants.GetSupporterOverviewLink})*");
            }
        }

        if (userInfo.Type != "user" && userInfo.Type != "subscriber")
        {
            description.AppendLine($"Last.fm {userInfo.Type}");
        }

        if (description.Length > 0)
        {
            response.Embed.WithDescription(description.ToString());
        }

        var lastFmStats = new StringBuilder();
        lastFmStats.AppendLine($"Name: **{userInfo.Name}**");
        lastFmStats.AppendLine($"Username: **[{userSettings.UserNameLastFm}]({Constants.LastFMUserUrl}{userSettings.UserNameLastFm})**");
        if (userInfo.Subscriber != 0)
        {
            lastFmStats.AppendLine("Last.fm Pro subscriber");
        }

        lastFmStats.AppendLine($"Country: **{userInfo.Country}**");

        lastFmStats.AppendLine($"Registered: **<t:{userInfo.Registered.Text}:D>** (<t:{userInfo.Registered.Text}:R>)");

        response.Embed.AddField("Last.fm info", lastFmStats.ToString(), true);

        var age = DateTimeOffset.FromUnixTimeSeconds(userInfo.Registered.Text);
        var totalDays = (DateTime.UtcNow - age).TotalDays;
        var avgPerDay = userInfo.Playcount / totalDays;

        var playcounts = new StringBuilder();
        playcounts.AppendLine($"Scrobbles: **{userInfo.Playcount}**");
        playcounts.AppendLine($"Tracks: **{userInfo.TrackCount}**");
        playcounts.AppendLine($"Albums: **{userInfo.AlbumCount}**");
        playcounts.AppendLine($"Artists: **{userInfo.ArtistCount}**");
        response.Embed.AddField("Playcounts", playcounts.ToString(), true);

        var allPlays = await this._playService.GetAllUserPlays(userSettings.UserId);

        var stats = new StringBuilder();
        if (userSettings.UserType != UserType.User)
        {
            var hasImported = this._playService.UserHasImported(allPlays);
            if (hasImported)
            {
                stats.AppendLine("User has most likely imported plays from external source");
            }
        }

        stats.AppendLine($"Average of **{Math.Round(avgPerDay, 1)}** scrobbles per day");

        stats.AppendLine($"Average of **{Math.Round((double)userInfo.AlbumCount / userInfo.ArtistCount, 1)}** albums and **{Math.Round((double)userInfo.TrackCount / userInfo.ArtistCount, 1)}** tracks per artist");

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true);

        if (topArtists.Any())
        {
            var amount = topArtists.OrderByDescending(o => o.UserPlaycount).Take(10).Sum(s => s.UserPlaycount);
            stats.AppendLine($"Top **10** artists make up **{Math.Round((double)amount.GetValueOrDefault(0) / userInfo.Playcount * 100, 1)}%** of scrobbles");
        }

        var topDay = allPlays.GroupBy(g => g.TimePlayed.DayOfWeek).MaxBy(o => o.Count());
        if (topDay != null)
        {
            stats.AppendLine($"Most active day of the week is **{topDay.Key.ToString()}**");
        }

        if (stats.Length > 0)
        {
            response.Embed.AddField("Stats", stats.ToString());
        }

        if (user.UserDiscogs != null)
        {
            var collection = new StringBuilder();

            collection.AppendLine($"{user.UserDiscogs.MinimumValue} min " +
                                  $"• {user.UserDiscogs.MedianValue} med " +
                                  $"• {user.UserDiscogs.MaximumValue} max");

            if (user.UserType != UserType.User)
            {
                var discogsCollection = await this._discogsService.GetUserCollection(userSettings.UserId);
                if (discogsCollection.Any())
                {
                    var collectionTypes = discogsCollection
                            .GroupBy(g => g.Release.Format)
                            .OrderByDescending(o => o.Count());
                    foreach (var type in collectionTypes)
                    {
                        collection.AppendLine($"**`{type.Key}` {StringService.GetDiscogsFormatEmote(type.Key)}** - **{type.Count()}** ");
                    }
                }
            }


            response.Embed.AddField("Your Discogs collection", collection.ToString());
        }

        var monthDescription = new StringBuilder();
        var monthGroups = allPlays
            .OrderByDescending(o => o.TimePlayed)
            .GroupBy(g => new { g.TimePlayed.Month, g.TimePlayed.Year });

        foreach (var month in monthGroups.Take(6))
        {
            if (!allPlays.Any(a => a.TimePlayed < DateTime.UtcNow.AddMonths(-month.Key.Month)))
            {
                break;
            }

            var time = await this._timeService.GetPlayTimeForPlays(month);
            monthDescription.AppendLine(
                $"**`{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month.Key.Month)}`** " +
                $"- **{month.Count()}** plays " +
                $"- **{StringExtensions.GetLongListeningTimeString(time)}**");
        }
        if (monthDescription.Length > 0)
        {
            response.Embed.AddField("Months", monthDescription.ToString());
        }

        if (userSettings.UserType != UserType.User)
        {
            var yearDescription = new StringBuilder();
            var yearGroups = allPlays
                .OrderByDescending(o => o.TimePlayed)
                .GroupBy(g => g.TimePlayed.Year);

            var totalTime = await this._timeService.GetPlayTimeForPlays(allPlays);
            if (totalTime.TotalSeconds > 0)
            {
                yearDescription.AppendLine(
                    $"` All`** " +
                    $"- **{allPlays.Count}** plays " +
                    $"- **{StringExtensions.GetLongListeningTimeString(totalTime)}");
            }

            foreach (var year in yearGroups)
            {
                var time = await this._timeService.GetPlayTimeForPlays(year);
                yearDescription.AppendLine(
                    $"`{year.Key}`** " +
                    $"- **{year.Count()}** plays " +
                    $"- **{StringExtensions.GetLongListeningTimeString(time)}");
            }
            if (yearDescription.Length > 0)
            {
                response.Embed.AddField("Years", $"**{yearDescription.ToString()}**");
            }
        }
        else
        {
            var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
            if (randomHintNumber == 1 && this._supporterService.ShowPromotionalMessage(context.ContextUser.UserType, context.DiscordGuild?.Id))
            {
                this._supporterService.SetGuildPromoCache(context.DiscordGuild?.Id);
                if (user.UserDiscogs == null)
                {
                    response.Embed.AddField("Years", $"*Want to see an overview of your scrobbles throughout the years? " +
                                                     $"[Get .fmbot supporter here.]({Constants.GetSupporterOverviewLink})*");
                }
                else
                {
                    response.Embed.AddField("Years", $"*Want to see an overview of your scrobbles throughout the years and your Discogs collection? " +
                                                     $"[Get .fmbot supporter here.]({Constants.GetSupporterOverviewLink})*");
                }
            }
        }

        var footer = new StringBuilder();
        if (user.Friends?.Count > 0)
        {
            footer.AppendLine($"Friends: {user.Friends?.Count}");
        }
        if (user.FriendedByUsers?.Count > 0)
        {
            footer.AppendLine($"Befriended by: {user.FriendedByUsers?.Count}");
        }
        if (footer.Length > 0)
        {
            response.Embed.WithFooter(footer.ToString());
        }

        return response;
    }

    public static EmbedBuilder GetRemoveDataEmbed(User userSettings, string prfx)
    {
        var description = new StringBuilder();
        description.AppendLine("**Are you sure you want to delete all your data from .fmbot?**");
        description.AppendLine("This will remove the following data:");

        description.AppendLine("- Account settings like your connected Last.fm account");

        if (userSettings.Friends?.Count > 0)
        {
            var friendString = userSettings.Friends?.Count == 1 ? "friend" : "friends";
            description.AppendLine($"- `{userSettings.Friends?.Count}` {friendString}");
        }

        if (userSettings.FriendedByUsers?.Count > 0)
        {
            var friendString = userSettings.FriendedByUsers?.Count == 1 ? "friendlist" : "friendlists";
            description.AppendLine($"- You from `{userSettings.FriendedByUsers?.Count}` other {friendString}");
        }

        description.AppendLine("- All crowns you've gained or lost");
        description.AppendLine("- All featured history");

        if (userSettings.UserType != UserType.User)
        {
            description.AppendLine($"- `{userSettings.UserType}` account status");
            description.AppendLine("*Account status has to be manually changed back by an .fmbot admin*");
        }

        description.AppendLine();
        description.AppendLine($"Spotify out of sync? Check `/outofsync`");
        description.AppendLine($"Changed Last.fm username? Run `/login`");
        description.AppendLine();

        if (prfx == "/")
        {
            description.AppendLine($"If you still wish to logout, please click 'confirm'.");
        }
        else
        {
            description.AppendLine($"Type `{prfx}remove confirm` to confirm deletion.");
        }

        var embed = new EmbedBuilder();
        embed.WithDescription(description.ToString());

        embed.WithFooter("Note: This will not delete any data from Last.fm, just from .fmbot.");

        return embed;
    }
}
