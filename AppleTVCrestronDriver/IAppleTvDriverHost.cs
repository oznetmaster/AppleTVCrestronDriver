// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

namespace AppleTV.CrestronDriver;

/// <summary>
/// The subset of the RAD <c>ABasicVideoServer</c>/<c>ABasicDriver</c> surface that
/// <see cref="AppleTvVideoServer"/>'s orchestration logic depends on. Extracted so that logic can be
/// unit-tested off-box against a fake implementation instead of a real RAD base class, which cannot be
/// constructed off-box.
/// </summary>
internal interface IAppleTvDriverHost
	{
	/// <summary>
	/// The model identifier used to scope the credential directory on disk.
	/// </summary>
	string BaseModel { get; }

	/// <summary>
	/// Reads a previously persisted setting value, or <see langword="null"/> if none is stored.
	/// </summary>
	object GetSetting (string key);

	/// <summary>
	/// Persists a setting value for later retrieval via <see cref="GetSetting"/>.
	/// </summary>
	void SaveSetting (string key, object value);

	/// <summary>
	/// Updates the displayed value of a user-facing status attribute.
	/// </summary>
	void ModifyUserAttribute (string attributeId, string description);

	/// <summary>
	/// Emits a diagnostic log line, gated on the driver's logging configuration.
	/// </summary>
	void LogDiagnostic (string message);
	}
