using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;
using Solitaire.Core;
using Xunit;

namespace Solitaire.Tests;

public class KlondikeTests
{
	private static Card C(int rank, Suit suit) => new(rank, suit);

	/// <summary>A deck whose first cards are as given and the rest in order.</summary>
	private static List<Card> DeckStarting(params Card[] top) =>
		top.Concat(Deck.Standard().Where(c => !top.Contains(c))).ToList();

	[Fact]
	public void SevenPilesOneToSevenWithTheTopCardsUp()
	{
		var game = new Klondike(Deck.Shuffled(new Random(1)));
		for (int p = 0; p < 7; p++)
		{
			Assert.Equal(p + 1, game.Tableau(p).Count);
			Assert.True(game.Tableau(p)[^1].Up);
			Assert.All(game.Tableau(p).Take(p), c => Assert.False(c.Up));
		}
		Assert.Equal(24, game.Stock.Count);
	}

	[Fact]
	public void TableauCardsGoOnesHigherOfTheOtherColour()
	{
		var fresh = new Klondike(Deck.Shuffled(new Random(2)));
		for (int from = 0; from < 7; from++)
			for (int to = 0; to < 7; to++)
			{
				var a = fresh.Tableau(from)[^1].Card;
				var b = fresh.Tableau(to)[^1].Card;
				bool fits = from != to && b.Rank == a.Rank + 1 && b.IsRed != a.IsRed;
				Assert.Equal(fits, fresh.IsLegal(new Move(PileRef.Tableau(from), PileRef.Tableau(to), 1)));
			}
	}

	[Fact]
	public void AcesStartTheFoundationsAndTheyBuildUpBySuit()
	{
		var game = new Klondike(DeckStarting(C(Card.Ace, Suit.Hearts)));
		var up = new Move(PileRef.Tableau(0), PileRef.Foundation(0), 1);
		Assert.True(game.IsLegal(up));
		game.Make(up);
		Assert.Equal(C(Card.Ace, Suit.Hearts), game.Foundation(0)[^1]);
		Assert.Empty(game.Tableau(0));
		// Only a king may go into the empty pile.
		for (int p = 1; p < 7; p++)
			Assert.Equal(game.Tableau(p)[^1].Card.Rank == Card.King, game.IsLegal(new Move(PileRef.Tableau(p), PileRef.Tableau(0), 1)));
	}

	[Fact]
	public void MovingTheTopCardOffTurnsTheOneUnder()
	{
		var random = new Random(3);
		for (int seed = 0; seed < 50; seed++)
		{
			var game = new Klondike(Deck.Shuffled(new Random(seed)));
			if (game.UsefulMoves().FirstOrDefault(m => m.From.Kind == PileKind.Tableau && m.From.Index > 0) is { Count: > 0 } move)
			{
				var turned = game.Make(move);
				Assert.NotNull(turned);
				Assert.True(game.Tableau(move.From.Index)[^1].Up);
				return;
			}
		}
		Assert.Fail("No deal had a move.");
	}

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	public void DrawingGoesThroughTheStockAndTurnsTheWasteBackOver(int drawCount)
	{
		var game = new Klondike(Deck.Shuffled(new Random(4)), drawCount);
		var order = game.Stock.Reverse().ToList();
		int draws = (24 + drawCount - 1) / drawCount;
		for (int i = 0; i < draws; i++)
			game.Draw();
		Assert.Empty(game.Stock);
		Assert.Equal(order, game.Waste.ToList());
		game.Draw();
		Assert.Empty(game.Waste);
		Assert.Equal(order, game.Stock.Reverse().ToList());
		Assert.Equal(1, game.Passes);
		Assert.False(game.Stuck);
		// A second pass with nothing moved: stuck.
		for (int i = 0; i <= draws; i++)
			game.Draw();
		Assert.True(game.Stuck);
	}

	[Fact]
	public void UndoPutsEverythingBack()
	{
		var game = new Klondike(Deck.Shuffled(new Random(5)));
		var before = Enumerable.Range(0, 7).Select(p => game.Tableau(p).ToList()).ToList();
		game.Draw();
		if (game.Suggest() is { } move)
			game.Make(move);
		while (game.CanUndo)
			game.Undo();
		Assert.Equal(24, game.Stock.Count);
		Assert.Empty(game.Waste);
		for (int p = 0; p < 7; p++)
			Assert.Equal(before[p], game.Tableau(p).ToList());
		Assert.Equal(0, game.Moves);
	}

	[Fact]
	public void SafeMovesOnlyRaiseCardsNothingNeeds()
	{
		for (int seed = 0; seed < 100; seed++)
		{
			var game = new Klondike(Deck.Shuffled(new Random(seed)));
			while (game.SafeMove() is { } move)
			{
				var card = game.Cards(move)[0];
				Assert.True(card.Rank <= 2 || Enumerable.Range(0, 4).Select(game.Foundation)
					.Count(f => f.Count >= card.Rank - 1 && f[0].IsRed != card.IsRed) == 2);
				game.Make(move);
			}
		}
	}

	[Fact]
	public void TheComputerWinsSomeDealsAndNeverCheats()
	{
		int won = 0;
		for (int seed = 0; seed < 100; seed++)
		{
			var game = new Klondike(Deck.Shuffled(new Random(seed)));
			if (Autoplay.Play(game))
			{
				won++;
				Assert.All(Enumerable.Range(0, 4), f => Assert.Equal(13, game.Foundation(f).Count));
			}
			// Every card is somewhere, once.
			var all = Enumerable.Range(0, 7).SelectMany(p => game.Tableau(p).Select(c => c.Card))
				.Concat(Enumerable.Range(0, 4).SelectMany(game.Foundation)).Concat(game.Stock).Concat(game.Waste).ToList();
			Assert.Equal(52, all.Distinct().Count());
		}
		Assert.InRange(won, 5, 100);
	}
}

public class AutoplayTests
{
	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	public void WinnableDealsAreWonByTheComputer(int drawCount)
	{
		var random = new Random(7);
		for (int i = 0; i < 5; i++)
			Assert.True(Autoplay.Play(new Klondike(Autoplay.WinnableDeal(random, drawCount), drawCount)));
	}
}
