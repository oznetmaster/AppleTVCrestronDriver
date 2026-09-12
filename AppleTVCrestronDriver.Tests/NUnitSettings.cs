// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using NUnit.Framework;

// Preserve MSTest's fresh fixture instance for every test and serialize loopback/static-state tests.
[assembly: FixtureLifeCycle (LifeCycle.InstancePerTestCase)]
[assembly: LevelOfParallelism (1)]