using Moq;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Models;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Models
{
	public class RoundStatusUpdaterTests
	{
		[Fact]
		public async Task RoundStatusUpdaterTestsAsync()
		{
			List<RoundState> roundStates = new();
			roundStates.Add(new RoundState(uint256.One, null, null, FeeRate.Zero, Phase.InputRegistration, null));

			using CancellationTokenSource cancellationTokenSource = new();
			var cancellationToken = cancellationTokenSource.Token;

			var mockApiClient = new Mock<IWabiSabiApiRequestHandler>();
			mockApiClient.Setup(apiClient => apiClient.GetStatusAsync(It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => roundStates.ToArray());

			using RoundStateUpdater roundStatusUpdater = new(TimeSpan.FromSeconds(1), mockApiClient.Object);
			await roundStatusUpdater.StartAsync(cancellationTokenSource.Token);

			var waitFirst = await roundStatusUpdater.CreateRoundAwaiter(rs => rs.Phase == Phase.InputRegistration, cancellationToken);
			Assert.Equal(uint256.One, waitFirst.Id);

			await roundStatusUpdater.StopAsync(cancellationToken);
		}
	}
}