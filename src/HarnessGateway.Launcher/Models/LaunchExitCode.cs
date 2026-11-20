/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

namespace HarnessGateway.Launcher.Models;

public enum LaunchExitCode
{
    Ok = 0,
    UsageError = 2,
    UnknownAgent = 3,
    HarnessUnavailable = 4,
    EndpointUnreachable = 5,
    RequiredModelMissing = 6,
    ConfigRestoreFailure = 7,
    UnknownProviderName = 8,
    UnsupportedSettingsSchema = 9,
    ConfigConflict = 10,
    UpdateAvailable = 11,
    CheckFailed = 12,
    TerminalRequired = 13,
}