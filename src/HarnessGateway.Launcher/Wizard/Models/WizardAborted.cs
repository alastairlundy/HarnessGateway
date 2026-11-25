/*
    HarnessGateway - Copyright (c) Alastair Lundy 2026
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

namespace HarnessGateway.Launcher.Wizard.Models;

/// <summary>The wizard aborted before collecting a launch input.</summary>
public sealed record WizardAborted(WizardAbortReason Reason) : WizardOutcome;