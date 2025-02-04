using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Genius.Models.User;
using Microsoft.Extensions.Options;
using Swan;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Builders;

public class PlayBuilder
{
    private readonly CensorService _censorService;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IUpdateService _updateService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly PlayService _playService;
    private readonly GenreService _genreService;
    private readonly SettingService _settingService;
    private readonly TimeService _timeService;
    private readonly TrackService _trackService;
    private readonly UserService _userService;
    private readonly CountryService _countryService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;
    private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;
    private InteractiveService Interactivity { get; }

    public PlayBuilder(
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IUpdateService updateService,
        LastFmRepository lastFmRepository,
        PlayService playService,
        SettingService settingService,
        UserService userService,
        WhoKnowsPlayService whoKnowsPlayService,
        CensorService censorService,
        WhoKnowsArtistService whoKnowsArtistService,
        WhoKnowsAlbumService whoKnowsAlbumService,
        WhoKnowsTrackService whoKnowsTrackService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings,
        TimeService timeService,
        GenreService genreService,
        TrackService trackService,
        CountryService countryService)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._lastFmRepository = lastFmRepository;
        this._playService = playService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._updateService = updateService;
        this._userService = userService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this._censorService = censorService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._whoKnowsAlbumService = whoKnowsAlbumService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this.Interactivity = interactivity;
        this._timeService = timeService;
        this._genreService = genreService;
        this._trackService = trackService;
        this._countryService = countryService;
    }

    public async Task<ResponseModel> NowPlayingAsync(
        ContextModel context,
        UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        string sessionKey = null;
        if (!userSettings.DifferentUser && !string.IsNullOrEmpty(context.ContextUser.SessionKeyLastFm))
        {
            sessionKey = context.ContextUser.SessionKeyLastFm;
        }

        Response<RecentTrackList> recentTracks;

        if (!userSettings.DifferentUser)
        {
            if (context.ContextUser.LastIndexed == null)
            {
                _ = this._indexService.IndexUser(context.ContextUser);
                recentTracks = await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFm,
                    useCache: true, sessionKey: sessionKey);
            }
            else
            {
                recentTracks = await this._updateService.UpdateUserAndGetRecentTracks(context.ContextUser);
            }
        }
        else
        {
            recentTracks =
                await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFm, useCache: true);
        }

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            var errorEmbed =
                GenericEmbedService.RecentScrobbleCallFailedBuilder(recentTracks, userSettings.UserNameLastFm);
            response.Embed = errorEmbed;
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        var embedType = context.ContextUser.FmEmbedType;

        Guild guild = null;
        if (context.DiscordGuild != null)
        {
            guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);
            if (guild?.FmEmbedType != null)
            {
                embedType = guild.FmEmbedType.Value;
            }

            if (guild != null)
            {
                await this._indexService.UpdateGuildUser(await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId),
                    context.ContextUser.UserId, guild);
            }
        }

        var totalPlaycount = recentTracks.Content.TotalAmount;

        var currentTrack = recentTracks.Content.RecentTracks[0];
        var previousTrack = recentTracks.Content.RecentTracks.Count > 1 ? recentTracks.Content.RecentTracks[1] : null;
        if (userSettings.DifferentUser)
        {
            totalPlaycount = recentTracks.Content.TotalAmount;
        }

        if (!userSettings.DifferentUser)
        {
            this._whoKnowsPlayService.AddRecentPlayToCache(context.ContextUser.UserId, currentTrack);
        }

        var requesterUserTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var embedTitle = !userSettings.DifferentUser
            ? $"{requesterUserTitle}"
            : $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}, requested by {requesterUserTitle}";

        var fmText = "";
        var footerText = await this._userService.GetFooterAsync(context.ContextUser.FmFooterOptions, userSettings,
            currentTrack.ArtistName, currentTrack.AlbumName, currentTrack.TrackName, currentTrack.Loved, totalPlaycount, guild);

        if (!userSettings.DifferentUser &&
            !currentTrack.NowPlaying &&
            currentTrack.TimePlayed.HasValue &&
            currentTrack.TimePlayed < DateTime.UtcNow.AddHours(-1) &&
            currentTrack.TimePlayed > DateTime.UtcNow.AddDays(-5))
        {
            footerText.Append($"Using Spotify and lagging behind? Check '{context.Prefix}outofsync' - ");
        }

        switch (embedType)
        {
            case FmEmbedType.TextMini:
            case FmEmbedType.TextFull:
                if (embedType == FmEmbedType.TextMini)
                {
                    fmText += StringService.TrackToString(currentTrack).FilterOutMentions();
                }
                else if (previousTrack != null)
                {
                    fmText += $"**Current track**:\n";

                    fmText += StringService.TrackToString(currentTrack).FilterOutMentions();

                    fmText += $"\n" +
                              $"**Previous track**:\n";

                    fmText += StringService.TrackToString(previousTrack).FilterOutMentions();
                }

                fmText +=
                    $"`{footerText}`";

                response.ResponseType = ResponseType.Text;
                response.Text = fmText;
                break;
            default:
                if (embedType == FmEmbedType.EmbedMini || embedType == FmEmbedType.EmbedTiny)
                {
                    fmText += StringService.TrackToLinkedString(currentTrack, context.ContextUser.RymEnabled);
                    response.Embed.WithDescription(fmText);
                }
                else if (previousTrack != null)
                {
                    response.Embed.AddField("Current:",
                        StringService.TrackToLinkedString(currentTrack, context.ContextUser.RymEnabled));
                    response.Embed.AddField("Previous:",
                        StringService.TrackToLinkedString(previousTrack, context.ContextUser.RymEnabled));
                }

                string headerText;
                if (currentTrack.NowPlaying)
                {
                    headerText = "Now playing - ";
                }
                else
                {
                    headerText = embedType == FmEmbedType.EmbedMini
                        ? "Last track for "
                        : "Last tracks for ";
                }

                headerText += embedTitle;

                if (!currentTrack.NowPlaying && currentTrack.TimePlayed.HasValue)
                {
                    footerText.AppendLine("Last scrobble:");
                    response.Embed.WithTimestamp(currentTrack.TimePlayed.Value);
                }

                response.EmbedAuthor.WithName(headerText);
                response.EmbedAuthor.WithUrl(recentTracks.Content.UserUrl);

                //if (guild != null && !userSettings.DifferentUser)
                //{
                //    var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingTrack(context.ContextUser.UserId,
                //        guild, currentTrack.ArtistName, currentTrack.TrackName);

                //    if (guildAlsoPlaying != null)
                //    {
                //        footerText += "\n";
                //        footerText += guildAlsoPlaying;
                //    }
                //}

                if (footerText.Length > 0)
                {
                    response.EmbedFooter.WithText(footerText.ToString());
                    response.Embed.WithFooter(response.EmbedFooter);
                }

                if (embedType != FmEmbedType.EmbedTiny)
                {
                    response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
                    response.Embed.WithAuthor(response.EmbedAuthor);
                    response.Embed.WithUrl(recentTracks.Content.UserUrl);
                }

                if (currentTrack.AlbumCoverUrl != null && embedType != FmEmbedType.EmbedTiny)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
                        currentTrack.AlbumName, currentTrack.ArtistName, currentTrack.AlbumCoverUrl);
                    if (safeForChannel == CensorService.CensorResult.Safe)
                    {
                        response.Embed.WithThumbnailUrl(currentTrack.AlbumCoverUrl);
                    }
                }

                break;
        }

        return response;
    }

    public async Task<ResponseModel> RecentAsync(
        ContextModel context,
        UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        string sessionKey = null;
        if (!userSettings.DifferentUser && !string.IsNullOrEmpty(context.ContextUser.SessionKeyLastFm))
        {
            sessionKey = context.ContextUser.SessionKeyLastFm;
        }

        Response<RecentTrackList> recentTracks;
        if (!userSettings.DifferentUser)
        {
            if (context.ContextUser.LastIndexed == null)
            {
                _ = this._indexService.IndexUser(context.ContextUser);
                recentTracks = await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFm,
                    useCache: true, sessionKey: sessionKey);
            }
            else
            {
                recentTracks = await this._updateService.UpdateUserAndGetRecentTracks(context.ContextUser);
            }
        }
        else
        {
            recentTracks =
                await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFm, 120, useCache: true);
        }

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            GenericEmbedService.RecentScrobbleCallFailedBuilder(recentTracks, userSettings.UserNameLastFm);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        var requesterUserTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var embedTitle = !userSettings.DifferentUser
            ? $"{requesterUserTitle}"
            : $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}, requested by {requesterUserTitle}";

        response.EmbedAuthor.WithName($"Latest tracks for {embedTitle}");

        if (!context.SlashCommand)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
        }
        response.EmbedAuthor.WithUrl(recentTracks.Content.UserRecentTracksUrl);
        response.Embed.WithAuthor(response.EmbedAuthor);

        var firstTrack = recentTracks.Content.RecentTracks.ElementAtOrDefault(0);
        string thumbnailUrl = null;
        if (firstTrack?.AlbumCoverUrl != null)
        {
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
                firstTrack.AlbumName, firstTrack.ArtistName, firstTrack.AlbumCoverUrl);
            if (safeForChannel == CensorService.CensorResult.Safe)
            {
                thumbnailUrl = firstTrack.AlbumCoverUrl;
            }
        }

        var pages = new List<PageBuilder>();

        var trackPages = recentTracks.Content.RecentTracks
            .Take(120)
            .ToList()
            .ChunkBy(6);
        var pageCounter = 1;

        foreach (var trackPage in trackPages)
        {
            var trackPageString = new StringBuilder();
            foreach (var track in trackPage)
            {
                trackPageString.AppendLine(StringService
                    .TrackToLinkedStringWithTimestamp(track, context.ContextUser.RymEnabled));
            }

            var footer = new StringBuilder();
            footer.Append($"Page {pageCounter}/{trackPages.Count}");
            footer.Append($" - {userSettings.UserNameLastFm} has {recentTracks.Content.TotalAmount} scrobbles");

            var page = new PageBuilder()
                .WithDescription(trackPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString());

            if (pageCounter == 1 && thumbnailUrl != null)
            {
                page.WithThumbnailUrl(thumbnailUrl);
            }

            pages.Add(page);

            pageCounter++;
        }

        if (!pages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("No recent tracks found.")
                .WithAuthor(response.EmbedAuthor));
        }

        response.StaticPaginator = StringService.BuildSimpleStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> StreakAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        User userWithStreak)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var recentTracks = await this._updateService.UpdateUserAndGetRecentTracks(userWithStreak);

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            GenericEmbedService.RecentScrobbleCallFailedBuilder(recentTracks, userSettings.UserNameLastFm);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        var streak = await this._playService.GetStreak(userSettings.UserId, recentTracks);
        var streakText = PlayService.StreakToText(streak);
        response.Embed.WithDescription(streakText);

        response.EmbedAuthor.WithName($"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}'s streak overview");
        if (!userSettings.DifferentUser)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
            var saved = await this._playService.UpdateOrInsertStreak(streak);
            response.Embed.WithFooter(saved);
        }

        response.EmbedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library");
        response.Embed.WithAuthor(response.EmbedAuthor);

        return response;
    }

    public async Task<ResponseModel> StreakHistoryAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        bool viewIds = false,
        long? streakToDelete = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var streaks = await this._playService.GetStreaks(userSettings.UserId);

        response.EmbedAuthor.WithName(
            !userSettings.DifferentUser
                ? $"Streak history for {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}"
                : $"Streak history for {userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}");

        response.EmbedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library");

        if (!streaks.Any())
        {
            response.Embed.WithDescription("No saved streaks found for this user.");
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        if (streakToDelete.HasValue && !userSettings.DifferentUser)
        {
            var streak = streaks.FirstOrDefault(f =>
                f.UserStreakId == streakToDelete && f.UserId == context.ContextUser.UserId);
            await this._playService.DeleteStreak(streak.UserStreakId);

            response.Embed.WithTitle("🗑 Streak deleted");
            response.Embed.WithDescription("Successfully deleted the following streak:\n" +
                                           PlayService.StreakToText(streak, false));
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var streakPages = streaks.Chunk(4).ToList();

        var counter = 1;
        var pageCounter = 1;
        var pages = new List<PageBuilder>();
        foreach (var page in streakPages)
        {
            var pageString = new StringBuilder();
            foreach (var streak in page)
            {
                pageString.Append($"**{counter}. **");

                if ((streak.StreakEnded - streak.StreakStarted).TotalHours <= 20)
                {
                    pageString.Append($"<t:{((DateTimeOffset)streak.StreakStarted).ToUnixTimeSeconds()}:f>");
                    pageString.Append($" til ");
                    pageString.Append($"<t:{((DateTimeOffset)streak.StreakEnded).ToUnixTimeSeconds()}:t>");
                }
                else
                {
                    pageString.Append($"<t:{((DateTimeOffset)streak.StreakStarted).ToUnixTimeSeconds()}:f>");
                    pageString.Append($" til ");
                    pageString.Append($"<t:{((DateTimeOffset)streak.StreakEnded).ToUnixTimeSeconds()}:f>");
                }

                if (viewIds && !userSettings.DifferentUser)
                {
                    pageString.Append($" · Deletion ID: `{streak.UserStreakId}`");
                }

                pageString.AppendLine();

                var streakText = PlayService.StreakToText(streak, false);
                pageString.AppendLine(streakText);

                counter++;
            }

            var pageFooter = new StringBuilder();
            pageFooter.Append($"Page {pageCounter}/{streakPages.Count}");

            pages.Add(new PageBuilder()
                .WithDescription(pageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(pageFooter.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        return response;
    }

    public async Task<ResponseModel> OverviewAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        int amount)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        await this._updateService.UpdateUser(context.ContextUser);

        var week = await this._playService.GetDailyOverview(userSettings.UserId, amount);

        if (week == null)
        {
            response.ResponseType = ResponseType.Text;
            response.Text = "Sorry, we don't have plays for this user in the selected amount of days.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        foreach (var day in week.Days.OrderByDescending(o => o.Date))
        {
            var genreString = new StringBuilder();
            if (day.TopGenres != null && day.TopGenres.Any())
            {
                for (var i = 0; i < day.TopGenres.Count; i++)
                {
                    if (i != 0)
                    {
                        genreString.Append(" - ");
                    }

                    var genre = day.TopGenres[i];
                    genreString.Append($"{genre}");
                }
            }

            response.Embed.AddField(
                $"{day.Playcount} {StringExtensions.GetPlaysString(day.Playcount)} - {StringExtensions.GetListeningTimeString(day.ListeningTime)} - <t:{day.Date.ToUnixEpochDate()}:D>",
                $"{genreString}\n" +
                $"{day.TopArtist}\n" +
                $"{day.TopAlbum}\n" +
                $"{day.TopTrack}"
            );
        }

        var description = $"Top genres, artist, album and track for last {amount} days";

        if (week.Days.Count < amount)
        {
            description += $"\n{amount - week.Days.Count} days not shown because of no plays.";
        }

        response.Embed.WithDescription(description);

        response.EmbedAuthor.WithName($"Daily overview for {Format.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}");

        response.EmbedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library?date_preset=LAST_7_DAYS");
        response.Embed.WithAuthor(response.EmbedAuthor);

        response.EmbedFooter.WithText($"{week.Uniques} unique tracks - {week.Playcount} total plays - avg {Math.Round(week.AvgPerDay, 1)} per day");
        response.Embed.WithFooter(response.EmbedFooter);

        return response;
    }

    public async Task<ResponseModel> PaceAsync(ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        long goalAmount,
        long userTotalPlaycount,
        long? registeredUnixTime = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text,
        };

        long? count;

        if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            timeSettings.TimeFrom = registeredUnixTime;
            count = userTotalPlaycount;
        }
        else
        {
            count = await this._lastFmRepository.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeSettings.TimeFrom, userSettings.SessionKeyLastFm);
        }

        if (count is null or 0)
        {
            response.Text = $"<@{context.DiscordUser.Id}> No plays found in the {timeSettings.Description} time period.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var age = DateTimeOffset.FromUnixTimeSeconds(timeSettings.TimeFrom.GetValueOrDefault());
        var totalDays = (DateTime.UtcNow - age).TotalDays;

        var playsLeft = goalAmount - userTotalPlaycount;

        var avgPerDay = count / totalDays;

        var goalDate = (DateTime.UtcNow.AddDays(playsLeft / avgPerDay.Value));

        var reply = new StringBuilder();

        var determiner = "your";
        if (userSettings.DifferentUser)
        {
            reply.Append($"<@{context.DiscordUser.Id}> My estimate is that the user '{userSettings.UserNameLastFm.FilterOutMentions()}'");
            determiner = "their";
        }
        else
        {
            reply.Append($"<@{context.DiscordUser.Id}> My estimate is that you");
        }

        reply.AppendLine($" will reach **{goalAmount}** scrobbles on **<t:{goalDate.ToUnixEpochDate()}:D>**.");

        if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            reply.AppendLine(
                $"This is based on {determiner} alltime avg of {Math.Round(avgPerDay.GetValueOrDefault(0), 1)} per day. ({count} in {Math.Round(totalDays, 0)} days)");
        }
        else
        {
            reply.AppendLine(
                $"This is based on {determiner} avg of {Math.Round(avgPerDay.GetValueOrDefault(0), 1)} per day in the last {Math.Round(totalDays, 0)} days ({count} total)");
        }

        response.Text = reply.ToString();
        return response;
    }

    public async Task<ResponseModel> MileStoneAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        long mileStoneAmount,
        long userTotalPlaycount)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var mileStonePlay = await this._lastFmRepository.GetMilestoneScrobbleAsync(userSettings.UserNameLastFm, userSettings.SessionKeyLastFm, userTotalPlaycount, mileStoneAmount);

        if (!mileStonePlay.Success || mileStonePlay.Content == null)
        {
            response.Embed.ErrorResponse(mileStonePlay.Error, mileStonePlay.Message, "milestone", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        var reply = new StringBuilder();

        reply.AppendLine(StringService.TrackToLinkedString(mileStonePlay.Content));

        var userTitle = $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}";

        response.Embed.WithTitle($"{mileStoneAmount}{StringExtensions.GetAmountEnd(mileStoneAmount)} scrobble from {userTitle}");

        if (mileStonePlay.Content.AlbumCoverUrl != null)
        {
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
                mileStonePlay.Content.AlbumName, mileStonePlay.Content.ArtistName, mileStonePlay.Content.AlbumCoverUrl);
            if (safeForChannel == CensorService.CensorResult.Safe)
            {
                response.Embed.WithThumbnailUrl(mileStonePlay.Content.AlbumCoverUrl);
            }
        }

        if (mileStonePlay.Content.TimePlayed.HasValue)
        {
            var dateString = mileStonePlay.Content.TimePlayed.Value.ToString("yyyy-M-dd");
            response.Embed.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library?from={dateString}&to={dateString}");

            reply.AppendLine($"Date played: **<t:{mileStonePlay.Content.TimePlayed.Value.ToUnixEpochDate()}:D>**");
        }

        response.Embed.WithDescription(reply.ToString());

        return response;
    }

    public async Task<ResponseModel> YearAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        int year)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        var pagesAmount = userSettings.UserType == UserType.User ? 2 : 3;

        var yearOverview = await this._playService.GetYear(userSettings.UserId, year);

        if (yearOverview.LastfmErrors)
        {
            response.Embed.WithDescription("Sorry, Last.fm returned an error. Please try again");
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }
        if (yearOverview.TopArtists?.TopArtists == null || !yearOverview.TopArtists.TopArtists.Any())
        {
            response.Embed.WithDescription("Sorry, you haven't listened to music in this year. If you think this message is wrong, please try again.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var userTitle = $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}'s";
        var pages = new List<PageBuilder>();

        var description = new StringBuilder();
        var fields = new List<EmbedFieldBuilder>();

        if (yearOverview.PreviousTopArtists?.TopArtists is { Count: > 0 })
        {
            description.AppendLine($"Your top genres, artists, albums and tracks for {year} compared to {year - 1}.");
        }
        else
        {
            description.AppendLine($"Welcome to Last.fm and .fmbot. Here's your overview for {year}.");
        }

        response.Embed.WithDescription(description.ToString());

        var genres = await this._genreService.GetTopGenresForTopArtists(yearOverview.TopArtists.TopArtists);

        var previousTopGenres = new List<TopGenre>();
        if (yearOverview.PreviousTopArtists?.TopArtists != null)
        {
            previousTopGenres = await this._genreService.GetTopGenresForTopArtists(yearOverview.PreviousTopArtists?.TopArtists);
        }

        var genreDescription = new StringBuilder();
        var lines = new List<StringService.BillboardLine>();
        for (var i = 0; i < genres.Count; i++)
        {
            var topGenre = genres[i];

            var previousTopGenre = previousTopGenres.FirstOrDefault(f => f.GenreName == topGenre.GenreName);

            int? previousPosition = previousTopGenre == null ? null : previousTopGenres.IndexOf(previousTopGenre);

            var line = StringService.GetBillboardLine($"**{topGenre.GenreName}**", i, previousPosition);
            lines.Add(line);

            if (i < 10)
            {
                genreDescription.AppendLine(line.Text);
            }
        }

        fields.Add(new EmbedFieldBuilder().WithName("Genres").WithValue(genreDescription.ToString()).WithIsInline(true));

        var artistDescription = new StringBuilder();
        for (var i = 0; i < yearOverview.TopArtists.TopArtists.Count; i++)
        {
            var topArtist = yearOverview.TopArtists.TopArtists[i];

            var previousTopArtist =
                yearOverview.PreviousTopArtists?.TopArtists?.FirstOrDefault(f =>
                    f.ArtistName == topArtist.ArtistName);

            var previousPosition = previousTopArtist == null ? null : yearOverview.PreviousTopArtists?.TopArtists?.IndexOf(previousTopArtist);

            var line = StringService.GetBillboardLine($"**{topArtist.ArtistName}**", i, previousPosition);
            lines.Add(line);

            if (i < 10)
            {
                artistDescription.AppendLine(line.Text);
            }
        }

        fields.Add(new EmbedFieldBuilder().WithName("Artists").WithValue(artistDescription.ToString()).WithIsInline(true));

        var rises = lines
            .Where(w => w.OldPosition is >= 20 && w.NewPosition <= 15 && w.PositionsMoved >= 15)
            .OrderBy(o => o.PositionsMoved)
            .ThenBy(o => o.NewPosition)
            .ToList();

        var risesDescription = new StringBuilder();
        if (rises.Any())
        {
            foreach (var rise in rises.Take(7))
            {
                risesDescription.Append($"<:5_or_more_up:912380324841918504>");
                risesDescription.AppendLine($"{rise.Name} (From #{rise.OldPosition} to #{rise.NewPosition})");
            }
        }

        if (risesDescription.Length > 0)
        {
            fields.Add(new EmbedFieldBuilder().WithName("Rises").WithValue(risesDescription.ToString()));
        }

        var drops = lines
            .Where(w => w.OldPosition is <= 15 && w.NewPosition >= 20 && w.PositionsMoved <= -15)
            .OrderBy(o => o.PositionsMoved)
            .ThenBy(o => o.OldPosition)
            .ToList();

        var dropsDescription = new StringBuilder();
        if (drops.Any())
        {
            foreach (var drop in drops.Take(7))
            {
                dropsDescription.Append($"<:5_or_more_down:912380324753838140> ");
                dropsDescription.AppendLine($"{drop.Name} (From #{drop.OldPosition} to #{drop.NewPosition})");
            }
        }

        if (dropsDescription.Length > 0)
        {
            fields.Add(new EmbedFieldBuilder().WithName("Drops").WithValue(dropsDescription.ToString()));
        }

        pages.Add(new PageBuilder()
            .WithFields(fields)
            .WithDescription(description.ToString())
            .WithTitle($"{userTitle} {year} in Review - 1/{pagesAmount}"));

        fields = new List<EmbedFieldBuilder>();

        var albumDescription = new StringBuilder();
        if (yearOverview.TopAlbums.TopAlbums.Any())
        {
            for (var i = 0; i < yearOverview.TopAlbums.TopAlbums.Take(8).Count(); i++)
            {
                var topAlbum = yearOverview.TopAlbums.TopAlbums[i];

                var previousTopAlbum =
                    yearOverview.PreviousTopAlbums?.TopAlbums?.FirstOrDefault(f =>
                        f.ArtistName == topAlbum.ArtistName && f.AlbumName == topAlbum.AlbumName);

                var previousPosition = previousTopAlbum == null ? null : yearOverview.PreviousTopAlbums?.TopAlbums?.IndexOf(previousTopAlbum);

                albumDescription.AppendLine(StringService.GetBillboardLine($"**{topAlbum.ArtistName}** - **{topAlbum.AlbumName}**", i, previousPosition).Text);
            }
            fields.Add(new EmbedFieldBuilder().WithName("Albums").WithValue(albumDescription.ToString()));
        }

        var trackDescription = new StringBuilder();
        for (var i = 0; i < yearOverview.TopTracks.TopTracks.Take(8).Count(); i++)
        {
            var topTrack = yearOverview.TopTracks.TopTracks[i];

            var previousTopTrack =
                yearOverview.PreviousTopTracks?.TopTracks?.FirstOrDefault(f =>
                    f.ArtistName == topTrack.ArtistName && f.TrackName == topTrack.TrackName);

            var previousPosition = previousTopTrack == null ? null : yearOverview.PreviousTopTracks?.TopTracks?.IndexOf(previousTopTrack);

            trackDescription.AppendLine(StringService.GetBillboardLine($"**{topTrack.ArtistName}** - **{topTrack.TrackName}**", i, previousPosition).Text);
        }

        fields.Add(new EmbedFieldBuilder().WithName("Tracks").WithValue(trackDescription.ToString()));

        var countries = await this._countryService.GetTopCountriesForTopArtists(yearOverview.TopArtists.TopArtists);

        var previousTopCountries = new List<TopCountry>();
        if (yearOverview.PreviousTopArtists?.TopArtists != null)
        {
            previousTopCountries = await this._countryService.GetTopCountriesForTopArtists(yearOverview.PreviousTopArtists?.TopArtists);
        }

        var countryDescription = new StringBuilder();
        for (var i = 0; i < countries.Count; i++)
        {
            var topCountry = countries[i];

            var previousTopCountry = previousTopCountries.FirstOrDefault(f => f.CountryCode == topCountry.CountryCode);

            int? previousPosition = previousTopCountry == null ? null : previousTopCountries.IndexOf(previousTopCountry);

            var line = StringService.GetBillboardLine($"**{topCountry.CountryName}**", i, previousPosition);
            lines.Add(line);

            if (i < 8)
            {
                countryDescription.AppendLine(line.Text);
            }
        }

        fields.Add(new EmbedFieldBuilder().WithName("Countries").WithValue(countryDescription.ToString()).WithIsInline(true));

        var tracksAudioOverview = await this._trackService.GetAverageTrackAudioFeaturesForTopTracks(yearOverview.TopTracks.TopTracks);
        var previousTracksAudioOverview = await this._trackService.GetAverageTrackAudioFeaturesForTopTracks(yearOverview.PreviousTopTracks?.TopTracks);

        if (tracksAudioOverview.Total > 0)
        {
            fields.Add(new EmbedFieldBuilder().WithName("Top track analysis")
                .WithValue(TrackService.AudioFeatureAnalysisComparisonString(tracksAudioOverview, previousTracksAudioOverview)));
        }

        var supporterDescription = context.ContextUser.UserType == UserType.User
            ? $"Want an extra page with your artist discoveries and a monthly overview? \n[Get .fmbot suppporter here.]({Constants.GetSupporterLink})"
            : "";

        pages.Add(new PageBuilder()
            .WithDescription(supporterDescription)
            .WithFields(fields)
            .WithTitle($"{userTitle} {year} in Review - 2/{pagesAmount}"));

        if (userSettings.UserType != UserType.User)
        {
            fields = new List<EmbedFieldBuilder>();

            var allPlays = await this._playService.GetAllUserPlays(userSettings.UserId);

            var filter = new DateTime(year, 01, 01);
            var endFilter = new DateTime(year, 12, 12, 23, 59, 59);
            var knownArtists = allPlays
                .Where(w => w.TimePlayed < filter)
                .GroupBy(g => g.ArtistName)
                .Select(s => s.Key)
                .ToList();

            var topNewArtists = allPlays
                .Where(w => w.TimePlayed >= filter && w.TimePlayed <= endFilter)
                .GroupBy(g => g.ArtistName)
                .Select(s => new TopArtist
                {
                    ArtistName = s.Key,
                    UserPlaycount = s.Count(),
                    FirstPlay = s.OrderBy(o => o.TimePlayed).First().TimePlayed
                })
                .Where(w => !knownArtists.Any(a => a.Equals(w.ArtistName)))
                .OrderByDescending(o => o.UserPlaycount)
                .Take(10)
                .ToList();

            var newArtistDescription = new StringBuilder();
            for (var i = 0; i < topNewArtists.Count(); i++)
            {
                var newArtist = topNewArtists.OrderBy(o => o.FirstPlay).ToList()[i];

                newArtistDescription.AppendLine($"**[{newArtist.ArtistName}]({newArtist})** " +
                                                $"- **{newArtist.UserPlaycount}** {StringExtensions.GetPlaysString(newArtist.UserPlaycount)} " +
                                                $"- On **<t:{newArtist.FirstPlay.Value.ToUnixEpochDate()}:D>**");
            }

            fields.Add(new EmbedFieldBuilder().WithName("Artist discoveries")
                .WithValue(newArtistDescription.ToString()));

            var monthDescription = new StringBuilder();
            var monthGroups = allPlays
                .Where(w => w.TimePlayed >= filter && w.TimePlayed <= endFilter)
                .OrderBy(o => o.TimePlayed)
                .GroupBy(g => new { g.TimePlayed.Month, g.TimePlayed.Year });

            foreach (var month in monthGroups)
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
                fields.Add(new EmbedFieldBuilder().WithName("Months")
                    .WithValue(monthDescription.ToString()));
            }

            pages.Add(new PageBuilder()
                .WithFields(fields)
                .WithTitle($"{userTitle} {year} in Review - 3/{pagesAmount}")
                .WithDescription("⭐ .fmbot Supporter stats"));
        }

        response.StaticPaginator = StringService.BuildSimpleStaticPaginator(pages);
        return response;
    }
}
