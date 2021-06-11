using Moq;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.RPC;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Crypto.ZeroKnowledge;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Crypto;
using WalletWasabi.WabiSabi.Crypto.CredentialRequesting;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.Tests.Helpers
{
	public static class WabiSabiFactory
	{
		private static readonly int MaxVsizeAllocationPerAlice = (1 << 8) - 1;

		public static Coin CreateCoin(Key key)
			=> CreateCoin(key, Money.Coins(1));

		public static Coin CreateCoin(Key key, Money amount)
			=> new(
				new OutPoint(Hashes.DoubleSHA256(key.PubKey.ToBytes()), 0),
				new TxOut(amount, key.PubKey.WitHash.ScriptPubKey));

		public static OwnershipProof CreateOwnershipProof(Key key, uint256? roundHash = null)
			=> OwnershipProof.GenerateCoinJoinInputProof(
				key,
				new CoinJoinInputCommitmentData("CoinJoinCoordinatorIdentifier", roundHash ?? BitcoinFactory.CreateUint256()));

		public static Round CreateRound(WabiSabiConfig cfg)
			=> new(new RoundParameters(
				cfg,
				Network.Main,
				new InsecureRandom(),
				new(100m)));

		public static Mock<IRPCClient> CreatePreconfiguredRpcClient(params Coin[] coins)
		{
			using Key key = new Key();
			var mockRpc = new Mock<IRPCClient>();
			mockRpc.Setup(rpc => rpc.GetTxOutAsync(It.IsAny<uint256>(), It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new NBitcoin.RPC.GetTxOutResponse
				{
					IsCoinBase = false,
					ScriptPubKeyType = "witness_v0_keyhash",
					Confirmations = 120,
					TxOut = new TxOut(Money.Coins(1), BitcoinFactory.CreateScript()),
				});
			foreach (var coin in coins)
			{
				mockRpc.Setup(rpc => rpc.GetTxOutAsync(coin.Outpoint.Hash, (int)coin.Outpoint.N, true))
					.ReturnsAsync(new NBitcoin.RPC.GetTxOutResponse
					{
						IsCoinBase = false,
						ScriptPubKeyType = "witness_v0_keyhash",
						Confirmations = 120,
						TxOut = coin.TxOut,
					});
			}
			mockRpc.Setup(rpc => rpc.EstimateSmartFeeAsync(It.IsAny<int>(), It.IsAny<EstimateSmartFeeMode>()))
				.ReturnsAsync(new EstimateSmartFeeResponse
				{
					Blocks = 1000,
					FeeRate = new FeeRate(10m)
				});
			mockRpc.Setup(rpc => rpc.GetMempoolInfoAsync(It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MemPoolInfo
				{
					MinRelayTxFee = 1
				});
			return mockRpc;
		}

		public static async Task<Arena> CreateAndStartArenaAsync()
			=> await CreateAndStartArenaAsync(
				new WabiSabiConfig(),
				CreatePreconfiguredRpcClient());

		public static async Task<Arena> CreateAndStartArenaAsync(WabiSabiConfig cfg, params Round[] rounds)
			=> await CreateAndStartArenaAsync(
				cfg,
				CreatePreconfiguredRpcClient(),
				rounds);

		public static async Task<Arena> CreateAndStartArenaAsync(WabiSabiConfig cfg, IMock<IRPCClient> mockRpc, params Round[] rounds)
		{
			Arena arena = new(TimeSpan.FromHours(1), Network.Main, cfg, mockRpc.Object, new Prison());
			foreach (var round in rounds)
			{
				arena.Rounds.Add(round.Id, round);
			}
			await arena.StartAsync(CancellationToken.None).ConfigureAwait(false);
			return arena;
		}

		public static Alice CreateAlice(Coin coin, OwnershipProof ownershipProof)
			=> new Alice(coin, ownershipProof) { Deadline = DateTimeOffset.UtcNow + TimeSpan.FromHours(1) };

		public static Alice CreateAlice(Key key, Money amount)
			=> CreateAlice(CreateCoin(key, amount), CreateOwnershipProof(key));

		public static Alice CreateAlice(Money amount)
			=> CreateAlice(new Key(), amount);

		public static Alice CreateAlice(Key key)
			=> CreateAlice(key, Money.Coins(1));

		public static Alice CreateAlice()
			=> CreateAlice(new Key(), Money.Coins(1));

		public static ArenaClient CreateArenaClient(Arena arena)
		{
			var roundState = RoundState.FromRound(arena.Rounds.First().Value);
			var random = new InsecureRandom();
			return new ArenaClient(
				roundState.CreateAmountCredentialClient(new ZeroCredentialPool(), random),
				roundState.CreateVsizeCredentialClient(new ZeroCredentialPool(), random),
				new ArenaRequestHandlerAdapter(arena));
		}

		public static InputRegistrationRequest CreateInputRegistrationRequest(Round round, Key? key = null, OutPoint? prevout = null)
		{
			(var amClient, var vsClient, _, _) = CreateWabiSabiClientsAndIssuers(round);
			var (zeroAmountCredentialRequest, _) = amClient.CreateRequestForZeroAmount();
			var (zeroVsizeCredentialRequest, _) = vsClient.CreateRequestForZeroAmount();

			return new(
				round.Id,
				prevout ?? BitcoinFactory.CreateOutPoint(),
				CreateOwnershipProof(key ?? new Key(), round.Id),
				zeroAmountCredentialRequest,
				zeroVsizeCredentialRequest);
		}

		public static (WabiSabiClient amountClient, WabiSabiClient vsizeClient, CredentialIssuer amountIssuer, CredentialIssuer vsizeIssuer) CreateWabiSabiClientsAndIssuers(Round round)
		{
			var rnd = new InsecureRandom();
			var amountIssuer = round.AmountCredentialIssuer;
			var vsizeIssuer = round.VsizeCredentialIssuer;
			var amountClient = new WabiSabiClient(
				amountIssuer.CredentialIssuerSecretKey.ComputeCredentialIssuerParameters(),
				rnd,
				amountIssuer.MaxAmount,
				new ZeroCredentialPool());

			var vsizeClient = new WabiSabiClient(
				vsizeIssuer.CredentialIssuerSecretKey.ComputeCredentialIssuerParameters(),
				rnd,
				vsizeIssuer.MaxAmount,
				new ZeroCredentialPool());

			EnsureZeroCredentials(amountClient, vsizeClient, amountIssuer, vsizeIssuer);

			return (amountClient, vsizeClient, amountIssuer, vsizeIssuer);
		}

		private static void EnsureZeroCredentials(WabiSabiClient amClient, WabiSabiClient vsClient, CredentialIssuer amIssuer, CredentialIssuer vsIssuer)
		{
			var (zeroAmountCredentialRequest, amVal) = amClient.CreateRequestForZeroAmount();
			var (zeroVsizeCredentialRequest, vsVal) = vsClient.CreateRequestForZeroAmount();
			var amCredResp = amIssuer.HandleRequest(zeroAmountCredentialRequest);
			var vsCredResp = vsIssuer.HandleRequest(zeroVsizeCredentialRequest);

			amClient.HandleResponse(amCredResp, amVal);
			vsClient.HandleResponse(vsCredResp, vsVal);
		}

		public static (Alice alice, RealCredentialsRequest amountRequest, RealCredentialsRequest vsizeRequest) CreateRealCredentialRequests(Round round, Money? amount = null, long? vsize = null)
		{
			var (amClient, vsClient, amIssuer, vsIssuer) = CreateWabiSabiClientsAndIssuers(round);

			var alice = round.Alices.FirstOrDefault() ?? CreateAlice();
			var (realAmountCredentialRequest, _) = amClient.CreateRequest(
				new[] { amount?.Satoshi ?? alice.CalculateRemainingAmountCredentials(round.FeeRate).Satoshi },
				Array.Empty<Credential>(),
				CancellationToken.None);
			var (realVsizeCredentialRequest, _) = vsClient.CreateRequest(
				new[] { vsize ?? alice.CalculateRemainingVsizeCredentials(round.MaxVsizeAllocationPerAlice) },
				Array.Empty<Credential>(),
				CancellationToken.None);

			return (alice, realAmountCredentialRequest, realVsizeCredentialRequest);
		}

		public static ConnectionConfirmationRequest CreateConnectionConfirmationRequest(Round round)
		{
			var (alice, realAmountCredentialRequest, realVsizeCredentialRequest) = CreateRealCredentialRequests(round);

			var (amClient, vsClient, amIssuer, vsIssuer) = CreateWabiSabiClientsAndIssuers(round);
			var (zeroAmountCredentialRequest, _) = amClient.CreateRequestForZeroAmount();
			var (zeroVsizeCredentialRequest, _) = vsClient.CreateRequestForZeroAmount();

			return new ConnectionConfirmationRequest(
				round.Id,
				alice.Id,
				zeroAmountCredentialRequest,
				realAmountCredentialRequest,
				zeroVsizeCredentialRequest,
				realVsizeCredentialRequest);
		}

		public static OutputRegistrationRequest CreateOutputRegistrationRequest(Round round, Script? script = null, int? vsize = null)
		{
			(var amClient, var vsClient, var amIssuer, var vsIssuer) = CreateWabiSabiClientsAndIssuers(round);

			var alice = round.Alices.FirstOrDefault() ?? CreateAlice();
			var (amCredentialRequest, amValid) = amClient.CreateRequest(
				new[] { alice.CalculateRemainingAmountCredentials(round.FeeRate).Satoshi },
				Array.Empty<Credential>(),
				CancellationToken.None);
			long startingVsizeCredentialAmount = alice.CalculateRemainingVsizeCredentials(round.MaxVsizeAllocationPerAlice);
			var (vsCredentialRequest, weValid) = vsClient.CreateRequest(
				new[] { startingVsizeCredentialAmount },
				Array.Empty<Credential>(),
				CancellationToken.None);

			var amResp = amIssuer.HandleRequest(amCredentialRequest);
			var weResp = vsIssuer.HandleRequest(vsCredentialRequest);
			var amountCredentials = amClient.HandleResponse(amResp, amValid);
			var vsizeCredentials = vsClient.HandleResponse(weResp, weValid);

			script ??= BitcoinFactory.CreateScript();
			var (realAmountCredentialRequest, _) = amClient.CreateRequest(
				Array.Empty<long>(),
				amountCredentials,
				CancellationToken.None);

			try
			{
				vsize ??= script.EstimateOutputVsize();
			}
			catch (NotImplementedException)
			{
				vsize = 25;
			}

			var (realVsizeCredentialRequest, _) = vsClient.CreateRequest(
				new[] { startingVsizeCredentialAmount - (long)vsize },
				vsizeCredentials,
				CancellationToken.None);

			return new OutputRegistrationRequest(
				round.Id,
				script,
				realAmountCredentialRequest,
				realVsizeCredentialRequest);
		}

		public static Round CreateBlameRound(Round round, WabiSabiConfig cfg)
			=> new(new(cfg, round.Network, new InsecureRandom(), round.FeeRate, blameOf: round));
	}
}
