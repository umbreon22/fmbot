using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;
using Serilog;

namespace FMBot.Bot.TextCommands;

[Name("Admin settings")]
[Summary(".fmbot Admins Only")]
[ExcludeFromHelp]
public class AdminCommands : BaseCommandModule
{
    private readonly AdminService _adminService;
    private readonly CensorService _censorService;
    private readonly GuildService _guildService;
    private readonly TimerService _timer;
    private readonly LastFmRepository _lastFmRepository;
    private readonly SupporterService _supporterService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly FeaturedService _featuredService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly StaticBuilders _staticBuilders;
    private readonly AlbumService _albumService;

    private InteractiveService Interactivity { get; }

    public AdminCommands(
        AdminService adminService,
        CensorService censorService,
        GuildService guildService,
        TimerService timer,
        LastFmRepository lastFmRepository,
        SupporterService supporterService,
        UserService userService,
        IOptions<BotSettings> botSettings,
        SettingService settingService,
        FeaturedService featuredService,
        IIndexService indexService,
        IPrefixService prefixService,
        StaticBuilders staticBuilders, InteractiveService interactivity, AlbumService albumService) : base(botSettings)
    {
        this._adminService = adminService;
        this._censorService = censorService;
        this._guildService = guildService;
        this._timer = timer;
        this._lastFmRepository = lastFmRepository;
        this._supporterService = supporterService;
        this._userService = userService;
        this._settingService = settingService;
        this._featuredService = featuredService;
        this._indexService = indexService;
        this._prefixService = prefixService;
        this._staticBuilders = staticBuilders;
        this.Interactivity = interactivity;
        this._albumService = albumService;
    }

    //[Command("debug")]
    //[Summary("Returns user data")]
    //[Alias("dbcheck")]
    //public async Task DebugAsync(IUser user = null)
    //{
    //    var chosenUser = user ?? this.Context.Message.Author;
    //    var userSettings = await this._userService.GetFullUserAsync(chosenUser);

    //    if (userSettings?.UserNameLastFM == null)
    //    {
    //        await ReplyAsync("The user's Last.fm name has not been set.");
    //        this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);
    //        return;
    //    }

    //    this._embed.WithTitle($"Debug for {chosenUser.ToString()}");

    //    var description = "";
    //    foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(userSettings))
    //    {
    //        var name = descriptor.Name;
    //        var value = descriptor.GetValue(userSettings);

    //        if (descriptor.PropertyType.Name == "ICollection`1")
    //        {
    //            continue;
    //        }

    //        if (value != null)
    //        {
    //            description += $"{name}: `{value}` \n";
    //        }
    //        else
    //        {
    //            description += $"{name}: null \n";
    //        }
    //    }

    //    description += $"Friends: `{userSettings.Friends.Count}`\n";
    //    description += $"Befriended by: `{userSettings.FriendedByUsers.Count}`\n";
    //    //description += $"Indexed artists: `{userSettings.Artists.Count}`";
    //    //description += $"Indexed albums: `{userSettings.Albums.Count}`";
    //    //description += $"Indexed tracks: `{userSettings.Tracks.Count}`";

    //    this._embed.WithDescription(description);
    //    await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
    //    this.Context.LogCommandUsed();
    //}


    [Command("serverdebug")]
    [Summary("Returns server data")]
    [Alias("guilddebug", "debugserver", "debugguild")]
    public async Task DebugGuildAsync(ulong? guildId = null)
    {
        var chosenGuild = guildId ?? this.Context.Guild.Id;
        var guild = await this._guildService.GetFullGuildAsync(chosenGuild, enableCache: false);

        if (guild == null)
        {
            await ReplyAsync("Guild does not exist in database");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        this._embed.WithTitle($"Debug for guild with id {chosenGuild}");

        var description = "";
        foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(guild))
        {
            var name = descriptor.Name;
            var value = descriptor.GetValue(guild);

            if (value == null)
            {
                description += $"{name}: null \n";
                continue;
            }

            if (descriptor.PropertyType.Name == "String[]")
            {
                var a = (Array)descriptor.GetValue(guild);
                var arrayValue = "";
                for (var i = 0; i < a.Length; i++)
                {
                    arrayValue += $"{a.GetValue(i)} - ";
                }

                if (a.Length > 0)
                {
                    description += $"{name}: `{arrayValue}` \n";
                }
                else
                {
                    description += $"{name}: null \n";
                }
            }
            else
            {
                description += $"{name}: `{value}` \n";
            }
        }

        this._embed.WithDescription(description);
        await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
        this.Context.LogCommandUsed();
    }

    [Command("issues")]
    [Summary("Toggles issue mode")]
    public async Task IssuesAsync([Remainder] string reason = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (!PublicProperties.IssuesAtLastFm || reason != null)
            {
                PublicProperties.IssuesAtLastFm = true;
                PublicProperties.IssuesReason = reason;
                await ReplyAsync("Enabled issue mode. This adds some warning messages, changes the bot status and disables full updates.\n" +
                                 $"Reason given: *\"{reason}\"*", allowedMentions: AllowedMentions.None);

            }
            else
            {
                PublicProperties.IssuesAtLastFm = false;
                PublicProperties.IssuesReason = null;
                await ReplyAsync("Disabled issue mode");
            }

            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins can change issue mode.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("purgecache")]
    [Summary("Purges discord caches")]
    public async Task PurgeCacheAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var client = this.Context.Client as DiscordShardedClient;
            if (client == null)
            {
                await ReplyAsync("Client is null");
                return;
            }

            var currentProcess = Process.GetCurrentProcess();
            var currentMemoryUsage = currentProcess.WorkingSet64;
            var reply = new StringBuilder();

            reply.AppendLine("Purged user cache and ran garbage collector.");
            reply.AppendLine($"Memory before purge: `{currentMemoryUsage.ToFormattedByteString()}`");

            foreach (var socketClient in client.Shards)
            {
                socketClient.PurgeUserCache();
            }

            GC.Collect();

            await Task.Delay(2000);

            currentProcess = Process.GetCurrentProcess();
            currentMemoryUsage = currentProcess.WorkingSet64;

            reply.AppendLine($"Memory after purge: `{currentMemoryUsage.ToFormattedByteString()}`");

            await ReplyAsync(reply.ToString());

            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins can change purge cache.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("addcensoredalbum")]
    [Summary("Adds censored album")]
    [Examples("addcensoredalbum Death Grips | No Love Deep Web")]
    public async Task AddCensoredAlbumAsync([Remainder] string albumValues)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            var albumSearch = await this._albumService.SearchAlbum(new ResponseModel(), this.Context.User, albumValues, userSettings.UserNameLastFM);
            if (albumSearch.Album == null)
            {
                await this.Context.SendResponse(this.Interactivity, albumSearch.Response);
                return;
            }

            var currentAlbum = await this._censorService.GetCurrentAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);
            if (currentAlbum is { SafeForCommands: false })
            {
                await ReplyAsync("That album is already censored");
                return;
            }

            await this._censorService.AddCensoredAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);

            await ReplyAsync($"Added `{albumSearch.Album.AlbumName}` by `{albumSearch.Album.ArtistName}` to the list of censored albums.", allowedMentions: AllowedMentions.None);
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins add censored albums.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("opencollectivesupporters", RunMode = RunMode.Async)]
    [Summary("Displays all .fmbot supporters.")]
    [Alias("ocsupporters")]
    public async Task AllSupportersAsync()
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync("Error: Insufficient rights.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var response = await this._staticBuilders.OpenCollectiveSupportersAsync(new ContextModel(this.Context, prfx, userSettings));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("addnsfwalbum")]
    [Summary("Adds nsfw album")]
    [Examples("addnsfwalbum Death Grips | No Love Deep Web")]
    public async Task AddNsfwAlbumAsync([Remainder] string albumValues)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            var albumSearch = await this._albumService.SearchAlbum(new ResponseModel(), this.Context.User, albumValues, userSettings.UserNameLastFM);
            if (albumSearch.Album == null)
            {
                await this.Context.SendResponse(this.Interactivity, albumSearch.Response);
                return;
            }

            var currentAlbum = await this._censorService.GetCurrentAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);
            if (currentAlbum is { SafeForFeatured: false })
            {
                await ReplyAsync("That album is already marked as nsfw");
                return;
            }
            await this._censorService.AddNsfwAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);

            await ReplyAsync($"Added `{albumSearch.Album.AlbumName}` by `{albumSearch.Album.ArtistName}` to the list of nsfw albums.", allowedMentions: AllowedMentions.None);
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins add nsfw albums.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("addcensoredartist")]
    [Summary("Adds censored artist")]
    [Examples("addcensoredartist Last Days of Humanity")]
    public async Task AddCensoredArtistAsync([Remainder] string artist)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (string.IsNullOrEmpty(artist))
            {
                await ReplyAsync("Enter a correct artist to be censored\n" +
                                 "Example: `.addcensoredartist \"Last Days of Humanity\"");
                return;
            }

            artist = artist.Replace("\"","");

            await this._censorService.AddCensoredArtist(artist);

            await ReplyAsync($"Added `{artist}` to the list of censored artists.");
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins add censored artists.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("checkbotted")]
    [Alias("checkbotteduser")]
    [Summary("Checks some stats for a user and if they're banned from global whoknows")]
    public async Task CheckBottedUserAsync(string user = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (string.IsNullOrEmpty(user))
            {
                await ReplyAsync("Enter an username to check\n" +
                                 "Example: `.fmcheckbotted Kefkef123`");
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var targetedUser = await this._settingService.GetUser(user, contextUser, this.Context);

            if (targetedUser.DifferentUser)
            {
                user = targetedUser.UserNameLastFm;
            }

            var bottedUser = await this._adminService.GetBottedUserAsync(user);

            var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(user);

            this._embed.WithTitle($"Botted check for Last.fm '{user}'");

            if (userInfo == null)
            {
                this._embed.WithDescription($"Not found on Last.fm - [User]({Constants.LastFMUserUrl}{user})");
            }
            else
            {
                this._embed.WithDescription($"[Profile]({Constants.LastFMUserUrl}{user}) - " +
                                            $"[Library]({Constants.LastFMUserUrl}{user}/library) - " +
                                            $"[Last.week]({Constants.LastFMUserUrl}{user}/listening-report) - " +
                                            $"[Last.year]({Constants.LastFMUserUrl}{user}/listening-report/year)");

                var dateAgo = DateTime.UtcNow.AddDays(-365);
                var timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();

                var count = await this._lastFmRepository.GetScrobbleCountFromDateAsync(user, timeFrom);

                var age = DateTimeOffset.FromUnixTimeSeconds(timeFrom);
                var totalDays = (DateTime.UtcNow - age).TotalDays;

                var avgPerDay = count / totalDays;
                this._embed.AddField("Avg scrobbles / day in last year", Math.Round(avgPerDay.GetValueOrDefault(0), 1));
            }

            this._embed.AddField("Banned from GlobalWhoKnows", bottedUser == null ? "No" : bottedUser.BanActive ? "Yes" : "No, but has been banned before");
            if (bottedUser != null)
            {
                this._embed.AddField("Reason / additional notes", bottedUser.Notes ?? "*No reason/notes*");
            }

            this._embed.WithFooter("Command not intended for use in public channels");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can check botted users");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("getusers")]
    [Examples("getusers frikandel_")]
    [GuildOnly]
    public async Task GetUsersForLastfmUserNameAsync(string userString = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild.SpecialGuild != true)
            {
                await ReplyAsync("This command can only be used in special guilds.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (string.IsNullOrEmpty(userString))
            {
                await ReplyAsync("Enter a Last.fm username to get the accounts for.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var otherUser = await this._settingService.GetDifferentUser(userString);
            if (otherUser != null && otherUser.UserNameLastFM.ToLower() != userString.ToLower())
            {
                userString = otherUser.UserNameLastFM;
            }

            var users = await this._adminService.GetUsersWithLfmUsernameAsync(userString);

            this._embed.WithTitle($"All .fmbot users with Last.fm username {userString}");
            this._embed.WithUrl($"https://www.last.fm/user/{userString}");

            foreach (var user in users)
            {
                var userDescription = new StringBuilder();

                if (user.SessionKeyLastFm != null)
                {
                    userDescription.AppendLine($"Authorized");
                }

                userDescription.AppendLine($"`{user.DiscordUserId}`");
                userDescription.AppendLine($"<@{user.DiscordUserId}>");

                if (user.LastUsed.HasValue)
                {
                    var specifiedDateTime = DateTime.SpecifyKind(user.LastUsed.Value, DateTimeKind.Utc);
                    var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                    userDescription.AppendLine($"Last used: <t:{dateValue}:R>.");
                }

                this._embed.AddField($"{user.UserId}", userDescription.ToString());
            }

            this._embed.WithFooter("Command not intended for use in public channels");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();

        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can use this command");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("addbotted")]
    [Alias("addbotteduser")]
    [Examples(".addbotteduser \"Kefkef123\" \"8 days listening time in Last.week\"")]
    public async Task AddBottedUserAsync(string user = null, string reason = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(reason))
            {
                await ReplyAsync("Enter an username and reason to remove someone from gwk banlist\n" +
                                 "Example: `.addbotteduser \"Kefkef123\" \"8 days listening time in Last.week\"`");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var bottedUser = await this._adminService.GetBottedUserAsync(user);
            if (bottedUser == null)
            {
                if (!await this._adminService.AddBottedUserAsync(user, reason))
                {
                    await ReplyAsync("Something went wrong while adding this user to the gwk banlist");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                }
                else
                {
                    await ReplyAsync($"User {user} has been banned from GlobalWhoKnows with reason `{reason.FilterOutMentions()}`", allowedMentions: AllowedMentions.None);
                    this.Context.LogCommandUsed();
                }
            }
            else
            {
                if (!await this._adminService.EnableBottedUserBanAsync(user, reason))
                {
                    await ReplyAsync("Something went wrong while adding this user to the gwk banlist");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                }
                else
                {
                    await ReplyAsync($"User {user} has been banned from GlobalWhoKnows with reason `{reason.FilterOutMentions()}`", allowedMentions: AllowedMentions.None);
                    this.Context.LogCommandUsed();
                }
            }


        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can remove botted users");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("removebotted")]
    [Alias("removebotteduser")]
    [Examples("removebotteduser \"Kefkef123\"")]
    public async Task RemoveBottedUserAsync(string user = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (string.IsNullOrEmpty(user))
            {
                await ReplyAsync("Enter an username to remove from the gwk banlist. This will flag their ban as `false`.\n" +
                                 "Example: `.removebotteduser \"Kefkef123\"`");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var bottedUser = await this._adminService.GetBottedUserAsync(user);
            if (bottedUser == null)
            {
                await ReplyAsync("The specified user has never been banned from GlobalWhoKnows");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (!bottedUser.BanActive)
            {
                await ReplyAsync("User is in banned user list, but their ban was already inactive");
                return;
            }

            if (!await this._adminService.DisableBottedUserBanAsync(user))
            {
                await ReplyAsync("The specified user has not been banned from GlobalWhoKnows");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }
            else
            {
                await ReplyAsync($"User {user} has been unbanned from GlobalWhoKnows");
                this.Context.LogCommandUsed();
                return;
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can remove botted users");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("addsupporter")]
    public async Task AddSupporterAsync(string user = null, string openCollectiveId = null, string sendDm = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            const string formatError = "Make sure to follow the correct format when adding a supporter\n" +
                                       "`.addsupporter \"discordUserId\" \"open-collective-id\"`\n" +
                                       "`.addsupporter \"278633844763262976\" \"03k0exgz-nm8yj64a-g4965wao-9r7b4dlv\"`\n\n" +
                                       "If you don't want the bot to send a thank you dm, add `\"nodm\"`\n" +
                                       "`.addsupporter \"278633844763262976\" \"03k0exgz-nm8yj64a-g4965wao-9r7b4dlv\" \"nodm\"`";

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(openCollectiveId) || user == "help")
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (!ulong.TryParse(user, out var discordUserId))
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();
            var userSettings = await this._userService.GetUserWithDiscogs(discordUserId);

            if (userSettings == null)
            {
                await ReplyAsync("`User not found`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }
            if (userSettings.UserType != UserType.User)
            {
                await ReplyAsync("`Can only change usertype of normal users`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var openCollectiveSupporter = await this._supporterService.GetOpenCollectiveSupporter(openCollectiveId);
            if (openCollectiveSupporter == null)
            {
                await ReplyAsync("`OpenCollective user not found`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var existingSupporters = await this._supporterService.GetAllSupporters();
            if (existingSupporters
                    .Where(w => w.OpenCollectiveId != null)
                    .FirstOrDefault(f => f.OpenCollectiveId.ToLower() == openCollectiveId.ToLower()) != null)
            {
                await ReplyAsync("`OpenCollective account already connected to someone else`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var supporter = await this._supporterService.AddOpenCollectiveSupporter(userSettings.DiscordUserId, openCollectiveSupporter);

            var addedRole = false;
            if (this.Context.Guild.Id == this._botSettings.Bot.BaseServerId)
            {
                try
                {
                    var guildUser = await this.Context.Guild.GetUserAsync(discordUserId);
                    if (guildUser != null)
                    {
                        var role = this.Context.Guild.Roles.FirstOrDefault(x => x.Name == "Supporter");
                        await guildUser.AddRoleAsync(role);
                        addedRole = true;
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Adding supporter role failed for {id}", discordUserId, e);
                }
            }

            this._embed.WithTitle("Added new supporter");
            var description = new StringBuilder();
            description.AppendLine($"User id: {user} | <@{user}>\n" +
                                   $"Name: **{supporter.Name}**\n" +
                                   $"Subscription type: `{Enum.GetName(supporter.SubscriptionType.GetValueOrDefault())}`");

            description.AppendLine();
            description.AppendLine(addedRole ? "✅ Supporter role added" : "❌ Unable to add supporter role");
            description.AppendLine("✅ Full update started");

            this._embed.WithFooter("Name changes go through OpenCollective and apply within 24h");

            var discordUser = await this.Context.Guild.GetUserAsync(discordUserId);
            if (discordUser != null && sendDm == null)
            {
                var thankYouEmbed = new EmbedBuilder();
                var thankYouMessage = new StringBuilder();
                thankYouMessage.AppendLine($"**Thank you for getting .fmbot {openCollectiveSupporter.SubscriptionType.ToString().ToLower()} supporter!**");
                thankYouMessage.AppendLine(openCollectiveSupporter.SubscriptionType == SubscriptionType.Lifetime
                    ? "Thanks to your purchase we can continue to improve and host the bot, while you get some nice perks in return. Here's a brief reminder of the features available to supporters:"
                    : "Thanks to your subscription we can continue to improve and host the bot, while you get some nice perks in return. Here's a brief reminder of the features available to supporters:");

                thankYouMessage.AppendLine();
                thankYouMessage.AppendLine("**Expanded statistics**\n" +
                                           "We've started a full update for you. " +
                                           "After a few minutes the following commands should be expanded:\n" +
                                           "- `artist`, `album` and `track` with first listen dates\n" +
                                           "- `stats` command with overall history\n" +
                                           "- `year` with artist discoveries and monthly overview\n" +
                                           "- `fm` footer with up to 8 options (configured with `/fmmode`)\n" +
                                           "- More coming soon");

                thankYouMessage.AppendLine();
                thankYouMessage.AppendLine("**Get featured**\n" +
                                           "Every first Sunday of the month is Supporter Sunday, where you have a higher chance of getting featured. " +
                                           $"The next Supporter Sunday is in {FeaturedService.GetDaysUntilNextSupporterSunday()} {StringExtensions.GetDaysString(FeaturedService.GetDaysUntilNextSupporterSunday())}.");

                if (userSettings.UserDiscogs != null)
                {
                    thankYouMessage.AppendLine();
                    thankYouMessage.AppendLine("**View your full Discogs collection**\n" +
                                               "If you use the `collection` command it will fetch your full collection from Discogs.\n" +
                                               $"This is also visible in other commands, like `artist`, `album`, `track` and `stats`.");
                }

                thankYouMessage.AppendLine();
                thankYouMessage.AppendLine("**Your info**\n" +
                                           $"Your name in the `supporters` command will be shown as `{supporter.Name}`. " +
                                           "This is also the name that will be shown when you sponsor charts. You can update this through your OpenCollective settings.");

                thankYouMessage.AppendLine();
                thankYouMessage.AppendLine("*You also get a badge after your name, a higher friend limit and more. You can view a full overview of your perks and info anytime by doing the `getsupporter` command.*");

                thankYouEmbed.WithDescription(thankYouMessage.ToString());
                await discordUser.SendMessageAsync(embed: thankYouEmbed.Build());
                description.AppendLine("✅ Thank you dm sent");
            }
            else
            {
                description.AppendLine("❌ Did not send thank you dm");
            }

            this._embed.WithDescription(description.ToString());

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();

            await this._indexService.IndexUser(userSettings);
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can add supporters");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("removesupporter")]
    public async Task RemoveSupporterAsync(string user = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var formatError = "Make sure to follow the correct format when removing a supporter\n" +
                              "`.removesupporter \"discord-user-id\"`";

            if (string.IsNullOrEmpty(user) || user == "help")
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (!ulong.TryParse(user, out var userId))
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var userSettings = await this._userService.GetUserAsync(userId);

            if (userSettings == null)
            {
                await ReplyAsync("`User not found`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }
            if (userSettings.UserType != UserType.Supporter)
            {
                await ReplyAsync("`User is not a supporter`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var existingSupporter = await this._supporterService.GetSupporter(userId);
            if (existingSupporter == null)
            {
                await ReplyAsync("`Existing supporter not found`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var supporter = await this._supporterService.OpenCollectiveSupporterExpired(existingSupporter);

            var removedRole = false;
            if (this.Context.Guild.Id == this._botSettings.Bot.BaseServerId)
            {
                try
                {
                    var guildUser = await this.Context.Guild.GetUserAsync(userId);
                    if (guildUser != null)
                    {
                        var role = this.Context.Guild.Roles.FirstOrDefault(x => x.Name == "Supporter");
                        await guildUser.RemoveRoleAsync(role);
                        removedRole = true;
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Removing supporter role failed for {id}", userId, e);
                }
            }

            this._embed.WithTitle("Processed supporter expiry");
            this._embed.WithDescription($"User id: {user} | <@{user}>\n" +
                                        $"Name: **{supporter.Name}**\n" +
                                        $"Subscription type: `{Enum.GetName(supporter.SubscriptionType.GetValueOrDefault())}`");

            this._embed.AddField("Bot was able to remove supporter role?",
                removedRole ? "Yes" : "No, please remove manually");

            this._embed.WithFooter("Full update has been started");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();

            await this._indexService.IndexUser(userSettings);
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can remove supporters");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("addsupporterclassic")]
    [Examples("addsupporter \"125740103539621888\" \"Drasil\" \"lifetime supporter\"", "addsupporter \"278633844763262976\" \"Aetheling\" \"monthly supporter (perm at 28-11-2021)\"")]
    public async Task AddSupporterClassicAsync(string user = null, string name = null, string internalNotes = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var formatError = "Make sure to follow the correct format when adding a supporter\n" +
                              "Examples: \n" +
                              "`.addsupporter \"125740103539621888\" \"Drasil\" \"lifetime supporter\"`\n" +
                              "`.addsupporter \"278633844763262976\" \"Aetheling\" \"monthly supporter (perm at 28-11-2021)\"`";

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(internalNotes) || string.IsNullOrEmpty(name) || user == "help")
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (!ulong.TryParse(user, out var userId))
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var userSettings = await this._userService.GetUserAsync(userId);

            if (userSettings == null)
            {
                await ReplyAsync("`User not found`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }
            if (userSettings.UserType != UserType.User)
            {
                await ReplyAsync("`Can only change usertype of normal users`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._supporterService.AddSupporter(userSettings.DiscordUserId, name, internalNotes);

            this._embed.WithDescription("Supporter added.\n" +
                                        $"User id: {user} | <@{user}>\n" +
                                        $"Name: **{name}**\n" +
                                        $"Internal notes: `{internalNotes}`");

            this._embed.WithFooter("Command not intended for use in public channels");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can add supporters");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("featuredoverride"), Summary("Changes the avatar to be an album.")]
    [Examples("featuredoverride \"imageurl\" \"description\" true")]
    public async Task FeaturedOverrideAsync(string url, string desc, bool stopTimer = false)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                await this._featuredService.CustomFeatured(this._timer._currentFeatured, desc, url);

                if (stopTimer)
                {
                    this._timer.Restart();
                    await Task.Delay(5000);
                    this._timer.Stop();
                }
                else
                {
                    this._timer.Restart();
                }

                var description = new StringBuilder();
                description.AppendLine($"Avatar: {url}");
                description.AppendLine($"Description: {desc}");
                description.AppendLine($"Timer stopped: {stopTimer}");

                this._embed.WithTitle("Featured override");
                this._embed.WithDescription(description.ToString());
                this._embed.WithFooter(
                    "You might also have to edit the next few hours in the database (with no update true)");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot owners can set featured.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("reconnectshard", RunMode = RunMode.Async)]
    [Summary("Reconnects a shard")]
    [GuildOnly]
    [ExcludeFromHelp]
    [Alias("reconnectshards")]
    [Examples("shard 0", "shard 821660544581763093")]
    public async Task ShardInfoAsync(ulong? guildId = null)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await this.Context.Channel.SendMessageAsync($"No permissions mate");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (!guildId.HasValue)
        {
            await this.Context.Channel.SendMessageAsync($"Enter a server id please (this server is `{this.Context.Guild.Id}`)");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var client = this.Context.Client as DiscordShardedClient;

        DiscordSocketClient shard;

        if (guildId is < 1000 and >= 0)
        {
            shard = client.GetShard(int.Parse(guildId.Value.ToString()));
        }
        else
        {
            var guild = client.GetGuild(guildId.Value);
            shard = client.GetShardFor(guild);
        }

        if (shard != null)
        {
            if (shard.ConnectionState == ConnectionState.Disconnected || shard.ConnectionState == ConnectionState.Disconnecting)
            {
                await this.Context.Channel.SendMessageAsync($"Connecting Shard #{shard.ShardId}");
                await shard.StartAsync();
                await this.Context.Channel.SendMessageAsync($"Connected Shard #{shard.ShardId}");
            }
            else
            {
                await this.Context.Channel.SendMessageAsync($"Shard #{shard.ShardId} is not in a disconnected state.");
            }
        }
        else
        {
            await this.Context.Channel.SendMessageAsync("Server or shard could not be found. \n" +
                                                        "This either means the bot is not connected to that server or that the bot is not in this server.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        this.Context.LogCommandUsed();
    }

    //[Command("fmavataroverride"), Summary("Changes the avatar to be a image from a link.")]
    //[Alias("fmsetavatar")]
    //public async Task fmavataroverrideAsync(string link, string desc = "Custom FMBot Avatar", int ievent = 0)
    //{
    //    if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
    //    {
    //        JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

    //        if (link == "help")
    //        {
    //            await ReplyAsync(cfgjson.Prefix + "fmavataroverride <image link> [message in quotation marks] [event 0 or 1]");
    //            return;
    //        }

    //        try
    //        {
    //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;

    //            if (ievent == 1)
    //            {
    //                _timer.UseCustomAvatarFromLink(client, link, desc, true);
    //                await ReplyAsync("Set avatar to '" + link + "' with description '" + desc + "'. This is an event and it cannot be stopped the without the Owner's assistance. To stop an event, please contact the owner of the bot or specify a different avatar without the event parameter.");
    //            }
    //            else
    //            {
    //                _timer.UseCustomAvatarFromLink(client, link, desc, false);
    //                await ReplyAsync("Set avatar to '" + link + "' with description '" + desc + "'. This is not an event.");
    //            }
    //        }
    //        catch (Exception e)
    //        {
    //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;
    //            ExceptionReporter.ReportException(client, e);
    //            await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
    //        }
    //    }
    //}

    //[Command("fmresetavatar"), Summary("Changes the avatar to be the default.")]
    //public async Task fmresetavatar()
    //{
    //    if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
    //    {
    //        try
    //        {
    //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;
    //            _timer.UseDefaultAvatar(client);
    //            await ReplyAsync("Set avatar to 'FMBot Default'");
    //        }
    //        catch (Exception e)
    //        {
    //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;
    //            ExceptionReporter.ReportException(client, e);
    //            await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
    //        }
    //    }
    //}

    [Command("resetfeatured")]
    [Summary("Restarts the featured timer.")]
    [Alias("restarttimer", "timerstart", "timerrestart")]
    public async Task RestartTimerAsync([Remainder] int? id = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                var feature = this._timer._currentFeatured;

                if (id.HasValue)
                {
                    feature = await this._featuredService.GetFeaturedForId(id.Value);
                }

                var updateDescription = new StringBuilder();
                updateDescription.AppendLine("**Selected feature**");
                updateDescription.AppendLine(feature.Description);
                updateDescription.AppendLine(feature.ImageUrl);
                updateDescription.AppendLine();

                var newFeature = await this._featuredService.ReplaceFeatured(feature, this.Context.User.Id);

                updateDescription.AppendLine("**New feature**");
                updateDescription.AppendLine(newFeature.Description);
                updateDescription.AppendLine(newFeature.ImageUrl);
                updateDescription.AppendLine();

                updateDescription.AppendLine("Featured timer restarted. Can take up to two minutes to show, max 3 times / hour");

                var dateValue = ((DateTimeOffset)feature.DateTime).ToUnixTimeSeconds();
                this._embed.AddField("Time", $"<t:{dateValue}:F>");

                this._embed.WithDescription(updateDescription.ToString());
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can restart timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("stoptimer")]
    [Summary("Stops the internal bot avatar timer.")]
    [Alias("timerstop")]
    public async Task StopTimerAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                this._timer.Stop();
                await ReplyAsync("Timer stopped");
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot owners can stop timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("timerstatus")]
    [Summary("Checks the status of the timer.")]
    public async Task TimerStatusAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                if (this._timer.IsTimerActive())
                {
                    await ReplyAsync("Timer is active");
                }
                else
                {
                    await ReplyAsync("Timer is inactive");
                }
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins can check timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("globalblacklistadd")]
    [Summary("Adds a user to the global FMBot blacklist.")]
    [Alias("globalblocklistadd")]
    public async Task BlacklistAddAsync(SocketGuildUser user = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (user == null)
                {
                    await ReplyAsync("Please specify what user you want to add to the blacklist.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                if (user == this.Context.Message.Author)
                {
                    await ReplyAsync("You cannot blacklist yourself!");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var blacklistResult = await this._adminService.AddUserToBlocklistAsync(user.Id);

                if (blacklistResult)
                {
                    await ReplyAsync("Added " + user.Username + " to the blacklist.", allowedMentions: AllowedMentions.None);
                }
                else
                {
                    await ReplyAsync("You have already added " + user.Username +
                                     " to the blacklist or the blacklist does not exist for this user.", allowedMentions: AllowedMentions.None);
                }

                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("globalblacklistremove")]
    [Summary("Removes a user from the global FMBot blacklist.")]
    [Alias("globalblocklistremove")]
    public async Task BlackListRemoveAsync(SocketGuildUser user = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (user == null)
                {
                    await ReplyAsync("Please specify what user you want to remove from the blacklist.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var blacklistResult = await this._adminService.RemoveUserFromBlocklistAsync(user.Id);

                if (blacklistResult)
                {
                    await ReplyAsync("Removed " + user.Username + " from the blacklist.", allowedMentions: AllowedMentions.None);
                }
                else
                {
                    await ReplyAsync("You have already removed " + user.Username + " from the blacklist.", allowedMentions: AllowedMentions.None);
                }
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("runfullupdate")]
    [Summary("Runs a full update for someone esle")]
    public async Task RunFullUpdate([Remainder] string user = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var userToUpdate = await this._settingService.GetDifferentUser(user);

                if (userToUpdate == null)
                {
                    await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                await ReplyAsync($"Running full update for '{userToUpdate.UserNameLastFM}'", allowedMentions: AllowedMentions.None);
                this.Context.LogCommandUsed();

                await this._indexService.IndexUser(userToUpdate);
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
