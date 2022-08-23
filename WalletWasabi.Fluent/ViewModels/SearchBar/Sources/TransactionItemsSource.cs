using System.Threading.Tasks;
using DynamicData;
using WalletWasabi.Fluent.ViewModels.SearchBar.Patterns;
using WalletWasabi.Fluent.ViewModels.SearchBar.SearchItems;
using WalletWasabi.Fluent.ViewModels.Wallets.Home.History;
using WalletWasabi.Fluent.ViewModels.Wallets.Home.History.HistoryItems;

namespace WalletWasabi.Fluent.ViewModels.SearchBar.Sources;

public class TransactionsSource : ISearchItemSource
{
	public IObservable<IChangeSet<ISearchItem, ComposedKey>> Changes =>
		TransactionsWatcher.Instance.TransactionChanges
			.Transform(ToSearchItem);

	private static ISearchItem ToSearchItem(TransactionEntry r)
	{
		var transactionId = r.HistoryItem.Id.ToString();
		var keywords = new[] { r.HistoryItem.Id.ToString() };
		return new ActionableItem(
			transactionId,
			$"Found in {r.Wallet.WalletName}",
			() =>
			{
				r.Wallet.NavigateAndHighlight(r.HistoryItem.Id);
				return Task.CompletedTask;
			},
			"Transactions",
			keywords)
		{
			Icon = GetIcon(r)
		};
	}

	private static string GetIcon(TransactionEntry transactionEntry)
	{
		return transactionEntry.HistoryItem switch
		{
			CoinJoinHistoryItemViewModel cj => "shield_regular",
			CoinJoinsHistoryItemViewModel cjs => "shield_regular",
			TransactionHistoryItemViewModel tx => "normal_transaction",
			_ => ""
		};
	}
}
