// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AppleTV.CrestronDriver;

/// <summary>
/// Receives tokenized commands relayed from the bridge server and applies them to the live
/// Companion Link session/connection this Crestron driver instance owns.
/// </summary>
internal interface IAppleTvBridgeCommandHandler
	{
	void HandleBridgeCommand (string commandLine);
	}

/// <summary>
/// A loopback-only TCP server, keyed by an Apple TV's stable identity, that relays a tokenized
/// text form of Apple TV commands and events between this Crestron driver (which owns the single
/// live Companion Link connection/pairing for that Apple TV) and any local client - namely the
/// Entity V2 extension driver, which no longer connects to the Apple TV itself and instead
/// proxies every command/event through this server.
/// </summary>
/// <remarks>
/// See <see cref="AppleTvBridgeProtocol"/> for the complete, authoritative tokenized line
/// vocabulary (commands, events, and the app-list encoding scheme) shared by both sides of the
/// bridge.
/// This server instance is kept alive in <see cref="AppleTvBridgeServerRegistry"/> across
/// Crestron Home reinitializing the owning driver instance, exactly like
/// <see cref="AppleTvPairingSessionState"/>, so an in-flight bridge connection is not torn down
/// by an unrelated reinitialization.
/// </remarks>
internal sealed class AppleTvBridgeServer : IDisposable
	{
	private readonly TcpListener _listener;
	private readonly ConcurrentDictionary<TcpClient, byte> _clients = new ();
	private readonly Action<string> _log;
	private volatile bool _disposed;
	private volatile IAppleTvBridgeCommandHandler _handler;

	private AppleTvBridgeServer (int port, Action<string> log)
		{
		_log = log;
		_listener = new TcpListener (IPAddress.Loopback, port);
		Port = port;
		}

	internal int Port { get; }

	/// <summary>
	/// The number of currently connected bridge clients. Exposed only so tests can deterministically
	/// wait for a just-opened client connection to be registered by the asynchronous accept loop
	/// before exercising broadcast/command behavior.
	/// </summary>
	internal int ConnectedClientCountForTests => _clients.Count;

	/// <summary>
	/// The current owning driver instance's command handler. Updated on every reinitialization
	/// so relayed commands are always applied to the currently live Companion Link session
	/// rather than one belonging to a superseded driver instance.
	/// </summary>
	internal IAppleTvBridgeCommandHandler Handler
		{
		get => _handler;
		set => _handler = value;
		}

	internal static AppleTvBridgeServer Start (int port, Action<string> log)
		{
		var server = new AppleTvBridgeServer (port, log);
		server._listener.Start ();
		server._log?.Invoke ($"Bridge server listening on 127.0.0.1:{port}.");
		_ = server.AcceptLoopAsync ();
		return server;
		}

	/// <summary>
	/// Binds the first available port from <paramref name="candidates"/> (see
	/// <see cref="AppleTvBridgePort.GetPortCandidates"/>), skipping any that are already in use
	/// by something else on this host, so the Crestron driver and the extension driver - which
	/// have no channel to coordinate a choice except the bridge port itself - always agree on
	/// which port is actually in use by walking the exact same deterministic sequence.
	/// </summary>
	/// <exception cref="SocketException">
	/// Every candidate port was already in use. This is expected to be extremely rare.
	/// </exception>
	internal static AppleTvBridgeServer StartFirstAvailable (IEnumerable<int> candidates, Action<string> log)
		{
		SocketException lastException = null;
		foreach (int candidatePort in candidates)
			{
			try
				{
				return Start (candidatePort, log);
				}
			catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
				{
				log?.Invoke ($"Bridge port {candidatePort} is already in use; trying the next deterministic fallback port.");
				lastException = exception;
				}
			}

		throw lastException ?? new SocketException ((int)SocketError.AddressAlreadyInUse);
		}

	private async System.Threading.Tasks.Task AcceptLoopAsync ()
		{
		while (!_disposed)
			{
			TcpClient client;
			try
				{
				client = await _listener.AcceptTcpClientAsync ().ConfigureAwait (false);
				}
			catch (Exception)
				{
				// Listener was stopped/disposed; exit quietly.
				return;
				}

			_clients[client] = 0;
			_log?.Invoke ("Bridge client connected.");
			_ = ReadLoopAsync (client);
			}
		}

	private async System.Threading.Tasks.Task ReadLoopAsync (TcpClient client)
		{
		try
			{
			using NetworkStream stream = client.GetStream ();
			var buffer = new byte[4096];
			var lineBuilder = new StringBuilder ();
			while (!_disposed)
				{
				int read = await stream.ReadAsync (buffer, 0, buffer.Length).ConfigureAwait (false);
				if (read <= 0)
					{
					break;
					}

				_ = lineBuilder.Append (Encoding.UTF8.GetString (buffer, 0, read));

				int newlineIndex;
				while ((newlineIndex = lineBuilder.ToString ().IndexOf ('\n')) >= 0)
					{
					string line = lineBuilder.ToString (0, newlineIndex).TrimEnd ('\r');
					_ = lineBuilder.Remove (0, newlineIndex + 1);
					if (!string.IsNullOrWhiteSpace (line))
						{
						try
							{
							_handler?.HandleBridgeCommand (line);
							}
						catch (Exception exception)
							{
							_log?.Invoke ($"Bridge command handling failed: {exception.Message}");
							}
						}
					}
				}
			}
		catch (Exception exception)
			{
			// Client disconnected or the connection faulted; fall through to cleanup.
			_log?.Invoke ($"Bridge client read loop faulted: {exception.GetType ().FullName}: {exception.Message}");
			}
		finally
			{
			_ = _clients.TryRemove (client, out _);
			try
				{
				client.Dispose ();
				}
			catch (Exception)
				{
				}

			_log?.Invoke ("Bridge client disconnected.");
			}
		}

	/// <summary>
	/// Sends a tokenized event line to every currently connected bridge client (i.e. every
	/// connected extension driver instance - normally just one).
	/// </summary>
	internal void BroadcastEvent (string eventLine)
		{
		if (_disposed)
			{
			return;
			}

		byte[] payload = Encoding.UTF8.GetBytes (eventLine + "\n");
		foreach (TcpClient client in new List<TcpClient> (_clients.Keys))
			{
			try
				{
				if (client.Connected)
					{
					NetworkStream stream = client.GetStream ();
					stream.Write (payload, 0, payload.Length);
					}
				}
			catch (Exception)
				{
				_ = _clients.TryRemove (client, out _);
				}
			}
		}

	public void Dispose ()
		{
		if (_disposed)
			{
			return;
			}

		_disposed = true;

		try
			{
			_listener.Stop ();
			}
		catch (Exception)
			{
			}

		foreach (TcpClient client in new List<TcpClient> (_clients.Keys))
			{
			try
				{
				client.Dispose ();
				}
			catch (Exception)
				{
				}
			}

		_clients.Clear ();
		}
	}

/// <summary>
/// Keeps a single <see cref="AppleTvBridgeServer"/> alive per Apple TV (keyed by
/// <see cref="AppleTvStoredDevice.UniqueId"/>) across Crestron Home reinitializing the owning
/// <c>AppleTvVideoServer</c> instance, so an in-flight local (extension driver) connection to the
/// bridge is never torn down just because the host recreated the driver.
/// </summary>
internal static class AppleTvBridgeServerRegistry
	{
	private static readonly ConcurrentDictionary<string, AppleTvBridgeServer> _servers = new (StringComparer.OrdinalIgnoreCase);
	private static readonly object _lock = new ();

	/// <summary>
	/// Returns the existing bridge server for <paramref name="uniqueId"/>, starting a new one
	/// bound to its deterministic port (see <see cref="AppleTvBridgePort"/>) if none is running
	/// yet.
	/// </summary>
	internal static AppleTvBridgeServer GetOrStart (string uniqueId, Action<string> log)
		{
		if (string.IsNullOrWhiteSpace (uniqueId))
			{
			throw new ArgumentException ("A stable Apple TV identifier is required.", nameof (uniqueId));
			}

		lock (_lock)
			{
			if (_servers.TryGetValue (uniqueId, out AppleTvBridgeServer existing))
				{
				return existing;
				}

			IEnumerable<int> candidates = AppleTvBridgePort.GetPortCandidates (uniqueId);
			AppleTvBridgeServer server = AppleTvBridgeServer.StartFirstAvailable (candidates, log);
			_servers[uniqueId] = server;
			return server;
			}
		}

	/// <summary>
	/// Stops and removes the bridge server for <paramref name="uniqueId"/>, if one is running.
	/// </summary>
	internal static void Stop (string uniqueId)
		{
		if (string.IsNullOrWhiteSpace (uniqueId))
			{
			return;
			}

		lock (_lock)
			{
			if (_servers.TryRemove (uniqueId, out AppleTvBridgeServer server))
				{
				server.Dispose ();
				}
			}
		}
	}

/// <summary>
/// Installs an <see cref="IAppleTvBridgeCommandHandler"/> on an <see cref="AppleTvBridgeServer"/>
/// and later clears it, but only if it is still the one currently registered.
/// </summary>
/// <remarks>
/// Exists to close a stale-handler race: <see cref="AppleTvBridgeServer"/> (and any local bridge
/// client connected to it, e.g. the extension driver) is kept alive across Crestron Home
/// reinitializing the owning <c>AppleTvVideoServer</c>/<c>AppleTvVideoServerProtocol</c> instance
/// (see <see cref="AppleTvBridgeServerRegistry"/>), but each such instance's own handler is not.
/// Without the "only if still current" guard, a superseded instance being disposed after a newer
/// instance has already installed its own handler would unconditionally null out that newer,
/// live handler - silently dropping every subsequent bridge command even though the newer
/// instance's Companion Link session is perfectly healthy. This type is deliberately independent
/// of the Crestron RAD SDK base classes so it can be exercised directly by unit tests.
/// </remarks>
internal sealed class AppleTvBridgeServerHandlerRegistration
	{
	private readonly AppleTvBridgeServer _bridgeServer;
	private readonly IAppleTvBridgeCommandHandler _handler;

	private AppleTvBridgeServerHandlerRegistration (AppleTvBridgeServer bridgeServer, IAppleTvBridgeCommandHandler handler)
		{
		_bridgeServer = bridgeServer;
		_handler = handler;
		}

	/// <summary>
	/// Installs <paramref name="handler"/> as the current handler on <paramref name="bridgeServer"/>
	/// and returns a registration that can later clear it if it is still current.
	/// </summary>
	internal static AppleTvBridgeServerHandlerRegistration Install (AppleTvBridgeServer bridgeServer, IAppleTvBridgeCommandHandler handler)
		{
		if (bridgeServer is null)
			{
			throw new ArgumentNullException (nameof (bridgeServer));
			}

		if (handler is null)
			{
			throw new ArgumentNullException (nameof (handler));
			}

		bridgeServer.Handler = handler;
		return new AppleTvBridgeServerHandlerRegistration (bridgeServer, handler);
		}

	/// <summary>
	/// Clears this registration's handler from its bridge server, but only if it is still the
	/// one currently installed; a no-op if a newer registration has since replaced it.
	/// </summary>
	internal void ClearIfCurrent ()
		{
		if (ReferenceEquals (_bridgeServer.Handler, _handler))
			{
			_bridgeServer.Handler = null;
			}
		}
	}

