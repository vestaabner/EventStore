// Copyright (c) 2012, Event Store LLP
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
// 
// Redistributions of source code must retain the above copyright notice,
// this list of conditions and the following disclaimer.
// Redistributions in binary form must reproduce the above copyright
// notice, this list of conditions and the following disclaimer in the
// documentation and/or other materials provided with the distribution.
// Neither the name of the Event Store LLP nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// 

using System;
using System.Collections.Generic;
using EventStore.Core.Tests.Helpers;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using TestFixtureWithExistingEvents =
    EventStore.Projections.Core.Tests.Services.core_projection.TestFixtureWithExistingEvents;

namespace EventStore.Projections.Core.Tests.Services.projection_core_service_command_reader
{
    public class specification_with_projection_core_service_command_reader : TestFixtureWithExistingEvents
    {
        private ProjectionCoreServiceCommandReader _commandReader;

        protected override void Given()
        {
            base.Given();
            AllWritesSucceed();
            NoOtherStreams();

            _commandReader = new ProjectionCoreServiceCommandReader(_bus, _ioDispatcher, Guid.NewGuid().ToString("N"));

            _bus.Subscribe<ProjectionCoreServiceMessage.StartCore>(_commandReader);
            _bus.Subscribe<ProjectionCoreServiceMessage.StopCore>(_commandReader);
        }

        protected override IEnumerable<WhenStep> PreWhen()
        {
            yield return CreateWriteEvent("$projections-$control", "$response-reader-started", "{}");
        }

        [SetUp]
        public new void SetUp()
        {
            WhenLoop();
        }

        protected override ManualQueue GiveInputQueue()
        {
            return new ManualQueue(_bus, _timeProvider);
        }
    }
}
