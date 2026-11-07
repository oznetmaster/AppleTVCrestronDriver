// Copyright (c) 2026 Neil Colvin. MIT with Commons Clause; see LICENSE.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using AppleTvControlLibrary.Connection;

namespace AppleTvControlLibrary.FakeDevice;

/// <summary>A loopback pairing peer that can alter an authentication response.</summary>
public sealed class PairingResponseTcpHost : IDisposable
	{
	private readonly TcpListener _listener = new (IPAddress.Loopback, 0);
	private readonly FakeCompanionDevice _device = new ();
	private readonly List<TcpClient> _clients = new ();
	private readonly Func<int, byte[], byte[]> _mutate;
	private readonly Task _acceptTask;
	private bool _disposed;

	/// <summary>Starts a synthetic pairing server.</summary>
	/// <param name="mutate">Transforms response TLV bytes, or returns null to close the connection.</param>
	public PairingResponseTcpHost (Func<int, byte[], byte[]> mutate)
		{
		_mutate = mutate;
		_listener.Start ();
		_acceptTask = AcceptAsync ();
		}

	/// <summary>Gets the loopback port.</summary>
	public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

	private async Task AcceptAsync ()
		{
		try
			{
			while (true)
				{
				TcpClient client = await _listener.AcceptTcpClientAsync ().ConfigureAwait (false);
				lock (_clients)
					{
					if (_disposed)
						{
						client.Dispose ();
						return;
						}
					_clients.Add (client);
					}
				_ = ServeAsync (client);
				}
			}
		catch (ObjectDisposedException) { }
		catch (SocketException) when (_disposed) { }
		}

	private async Task ServeAsync (TcpClient client)
		{
		try
			{
			using NetworkStream stream = client.GetStream ();
			var connection = new CompanionConnection ();
			connection.FrameReceived += (sender, type, payload) =>
				{
				var request = (Dictionary<object, object>)Opack.Opack.Unpack (payload, out _);
				(FrameType responseType, byte[] pairing) = _device.HandleAuthFrame (type, (byte[])request["_pd"]);
				byte[] changed = _mutate ((int)responseType, pairing);
				if (changed is null)
					{
					client.Close ();
					return;
					}
				var response = new Dictionary<string, object> { ["_pd"] = changed };
				byte[] frame = connection.BuildFrame (responseType, Opack.Opack.Pack (response));
				stream.Write (frame, 0, frame.Length);
				};
			var buffer = new byte[4096];
			int count;
			while ((count = await stream.ReadAsync (buffer, 0, buffer.Length).ConfigureAwait (false)) > 0)
				{
				var frame = new byte[count];
				Array.Copy (buffer, frame, count);
				connection.ReceiveData (frame);
				}
			}
		catch (Exception) { }
		finally
			{
			client.Dispose ();
			lock (_clients) _clients.Remove (client);
			}
		}

	/// <inheritdoc/>
	public void Dispose ()
		{
		lock (_clients)
			{
			_disposed = true;
			_listener.Stop ();
			foreach (TcpClient client in _clients) client.Dispose ();
			}
		_acceptTask.GetAwaiter ().GetResult ();
		}
	}
