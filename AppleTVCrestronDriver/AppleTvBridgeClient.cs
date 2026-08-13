// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppleTV.CrestronDriver;

/// <summary>
/// A plain <see cref="TcpClient"/>-based client for the loopback bridge exposed by the Apple TV
/// Companion Link video server driver's bridge server. Sends tokenized <c>CMD:</c> lines (see
/// <see cref="AppleTvBridgeProtocol"/>) and raises <see cref="LineReceived"/> for every
/// tokenized <c>EVT:</c> line the server broadcasts. Deliberately has no dependency on the
/// Companion Link control library, Crestron RAD base classes, or the Entity V2 SDK, so it is
/// usable and independently testable from both the Crestron video server driver test project
/// and the Entity V2 extension driver, which is expected to be its only production consumer.
/// </summary>
internal sealed class AppleTvBridgeClient : IDisposable
	{
	private readonly Action<string> _log;
	private TcpClient _client;
	private CancellationTokenSource _readCancellation;
	private volatile bool _disposed;

	internal AppleTvBridgeClient (Action<string> log = null)
		{
		_log = log;
		}

	/// <summary>
	/// Raised for every complete tokenized line (normally an <c>EVT:...</c> line) received from
	/// the bridge server, exactly as sent - no parsing is done here so callers can apply
	/// whatever subset of <see cref="AppleTvBridgeProtocol"/> they care about.
	/// </summary>
	internal event Action<string> LineReceived;

	/// <summary>
	/// Raised when the connection to the bridge server is lost, whether because the server
	/// closed it or because of a transport fault. Never raised for this client's own
	/// <see cref="Dispose"/>.
	/// </summary>
	internal event Action Disconnected;

	internal bool IsConnected => _client is { Connected: true } && !_disposed;

	/// <summary>
	/// Connects to the bridge server listening on the loopback address at <paramref name="port"/>
	/// and starts the background read loop that raises <see cref="LineReceived"/>.
	/// </summary>
	internal async Task ConnectAsync (int port, CancellationToken cancellationToken)
		{
		if (_disposed)
			{
			throw new ObjectDisposedException (nameof (AppleTvBridgeClient));
			}

		var client = new TcpClient ();
		using (cancellationToken.Register (state => ((TcpClient)state).Close (), client))
			{
			await client.ConnectAsync (IPAddress.Loopback, port).ConfigureAwait (false);
			}

		_client = client;
		_readCancellation = new CancellationTokenSource ();
		_log?.Invoke ($"Bridge client connected to 127.0.0.1:{port}.");
		_ = ReadLoopAsync (client, _readCancellation.Token);
		}

	/// <summary>
	/// Sends a tokenized command line (e.g. <c>CMD:HID:Select</c>) to the bridge server.
	/// A no-op if not currently connected.
	/// </summary>
	internal async Task SendCommandAsync (string commandLine)
		{
		TcpClient client = _client;
		if (client is not { Connected: true })
			{
			_log?.Invoke ($"Not sending bridge command '{commandLine}': not currently connected.");
			return;
			}

		byte[] payload = Encoding.UTF8.GetBytes (commandLine + "\n");
		try
			{
			NetworkStream stream = client.GetStream ();
			await stream.WriteAsync (payload, 0, payload.Length).ConfigureAwait (false);
			_log?.Invoke ($"Sent bridge command '{commandLine}'.");
			}
		catch (Exception exception)
			{
			_log?.Invoke ($"Failed to send bridge command '{commandLine}': {exception.Message}");
			}
		}

	private async Task ReadLoopAsync (TcpClient client, CancellationToken cancellationToken)
		{
		try
			{
			using NetworkStream stream = client.GetStream ();
			var buffer = new byte[4096];
			var lineBuilder = new StringBuilder ();
			while (!_disposed && !cancellationToken.IsCancellationRequested)
				{
				int read = await stream.ReadAsync (buffer, 0, buffer.Length, cancellationToken).ConfigureAwait (false);
				if (read <= 0)
					{
					break;
					}

				lineBuilder.Append (Encoding.UTF8.GetString (buffer, 0, read));

				int newlineIndex;
				while ((newlineIndex = lineBuilder.ToString ().IndexOf ('\n')) >= 0)
					{
					string line = lineBuilder.ToString (0, newlineIndex).TrimEnd ('\r');
					lineBuilder.Remove (0, newlineIndex + 1);
					if (!string.IsNullOrWhiteSpace (line))
						{
						try
							{
							LineReceived?.Invoke (line);
							}
						catch (Exception exception)
							{
							_log?.Invoke ($"Bridge line handling failed: {exception.Message}");
							}
						}
					}
				}
			}
		catch (Exception exception)
			{
			// Connection faulted or was cancelled/disposed; fall through to raise Disconnected.
			_log?.Invoke ($"Bridge client read loop faulted: {exception.GetType ().FullName}: {exception.Message}");
			}
		finally
			{
			if (!_disposed)
				{
				_log?.Invoke ("Bridge client disconnected from server.");
				Disconnected?.Invoke ();
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
			_readCancellation?.Cancel ();
			}
		catch (Exception)
			{
			}

		try
			{
			_client?.Close ();
			}
		catch (Exception)
			{
			}

			_readCancellation?.Dispose ();
				}
			}
