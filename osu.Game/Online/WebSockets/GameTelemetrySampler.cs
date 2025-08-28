// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.SelectV2;

namespace osu.Game.Online.WebSockets
{
    public static class GameTelemetryStates
    {
        public const string MENU = "menu";
        public const string SONG_SELECT = "song_select";
        public const string IN_GAME = "in_game";
        public const string RESULTS = "results";
        public const string LOADER = "loader";

        public const string UNKNOWN = "unknown";
    }

    public partial class GameTelemetrySampler : IDisposable
    {
        private readonly WebSocketBroadcastServer server;
        private readonly OsuGame game;

        public GameTelemetrySampler(OsuGame game, WebSocketBroadcastServer server)
        {
            this.server = server;
            this.game = game;
        }

        private CancellationTokenSource cts = new();

        public void Start()
        {
            _ = Task.Run(() => mainLoop(cts.Token), cts.Token);
        }

        public void Dispose()
        {
            try { cts?.Cancel(); } catch { }
            try { cts?.Dispose(); } catch { }
        }

        private async Task mainLoop(CancellationToken token)
        {
            var period = TimeSpan.FromSeconds(1.0 / 120.0);
            var sw = Stopwatch.StartNew();
            var next = sw.Elapsed;

            while (!token.IsCancellationRequested)
            {
                string state = updateState();
                var (score, accuracy) = sampleScore();

                // простейшая сериализация
                string json = JsonConvert.SerializeObject(new { state, score, accuracy });
                try { server.Broadcast(json); } catch { /* игнор */ }

                next += period;
                var delay = next - sw.Elapsed;
                if (delay <= TimeSpan.Zero)
                {
                    // отстали — не ждём, поправим фазу
                    next = sw.Elapsed;
                    await Task.Yield();
                }
                else
                {
                    try { await Task.Delay(delay, token).ConfigureAwait(false); }
                    catch (TaskCanceledException) { break; }
                }
            }
        }



        private string updateState()
        {
            var current = game?.PublicScreenStack?.CurrentScreen;
            return current switch
            {
                MainMenu => GameTelemetryStates.MENU,
                SongSelect => GameTelemetryStates.SONG_SELECT,
                Player => GameTelemetryStates.IN_GAME,
                ResultsScreen => GameTelemetryStates.RESULTS,
                PlayerLoader => GameTelemetryStates.LOADER,
                _ => GameTelemetryStates.UNKNOWN
            };
        }

        private (long, double) sampleScore()
        {
            if (game?.PublicScreenStack?.CurrentScreen is not Player p)
                return (0, 0);

            var sp = p.PublicScoreProcessor;
            if (sp == null) return (0, 0);

            try
            {
                long s = sp.TotalScore.Value;
                double a = sp.Accuracy.Value * 100.0;
                return (s, a);
            }
            catch { return (0, 0); }
        }
    }
}
