// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fleck;
using osu.Framework.Logging;
using osu.Game.Screens.Play;

namespace osu.Game.Online.WebSockets
{
    /// <summary>
    /// Минимальный WS-сервер широковещательной рассылки.
    /// Стартует на ws://127.0.0.1:7272/ (по умолчанию).
    /// </summary>
    public class WebSocketBroadcastServer : IDisposable
    {
        private readonly string url;
        private WebSocketServer? server;
        private readonly HashSet<IWebSocketConnection> clients = new HashSet<IWebSocketConnection>();
        private readonly object gate = new object();
        private int isRunning = 0;
        private readonly OsuGame game;

        public WebSocketBroadcastServer(OsuGame game, string url = "ws://127.0.0.1:7272")
        {
            this.url = url;
            this.game = game;
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref isRunning, 1) == 1) return;

            server = new WebSocketServer(url);
            // Разрешаем только localhost — безопаснее
            server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    lock (gate) clients.Add(socket);
                    Logger.Log("WS: client connected", LoggingTarget.Network, Framework.Logging.LogLevel.Verbose, true);
                };
                socket.OnMessage = message =>
                {
                    switch (message)
                    {
                        case "pause":
                            suspend();
                            break;
                        case "resume":
                            resume();
                            break;
                    }
                    Logger.Log("WS: client message", LoggingTarget.Network, Framework.Logging.LogLevel.Verbose, true);
                };
                socket.OnClose = () =>
                {
                    lock (gate) clients.Remove(socket);
                    Logger.Log("WS: client disconnected", LoggingTarget.Network, Framework.Logging.LogLevel.Verbose, true);
                };
                socket.OnError = (ex) =>
                {
                    // Молча удаляем "битых" клиентов
                    lock (gate) clients.Remove(socket);
                    Logger.Log("WS: client error", LoggingTarget.Network, Framework.Logging.LogLevel.Verbose, true);
                };
                // Сообщения от клиента нам не нужны — это чистая телеметрия
            });
        }

        public void Broadcast(string message)
        {
            IWebSocketConnection[] snapshot;
            lock (gate) snapshot = new List<IWebSocketConnection>(clients).ToArray();

            foreach (var c in snapshot)
            {
                try { c.Send(message); }
                catch { /* игнорируем сбои отдельных клиентов */ }
            }
        }

        public Task BroadcastAsync(string message)
        {
            return Task.Run(() => Broadcast(message));
        }

        private void suspend()
        {
            if (game?.PublicScreenStack?.CurrentScreen is Player p)
            {
                Logger.Log("WS: Clock suspend", LoggingTarget.Runtime, Framework.Logging.LogLevel.Verbose, true);
                p.PublicGameplayClockContainer.Stop();
            }
        }
        private void resume()
        {
            if (game?.PublicScreenStack?.CurrentScreen is Player p)
            {
                Logger.Log("WS: Clock resume", LoggingTarget.Runtime, Framework.Logging.LogLevel.Verbose, true);
                p.PublicGameplayClockContainer.Start();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                foreach (var c in clients) { try { c.Close(); } catch { } }
                clients.Clear();
            }
            try { server?.Dispose(); } catch { }
            isRunning = 0;
        }
    }
}
