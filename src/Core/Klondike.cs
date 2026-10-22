using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace Solitaire.Core;

/// <summary>Where a card can be: the stock, the waste, a foundation (0–3) or a tableau pile (0–6).</summary>
public enum PileKind { Stock, Waste, Foundation, Tableau }

public readonly record struct PileRef(PileKind Kind, int Index = 0)
{
	public static readonly PileRef Stock = new(PileKind.Stock);
	public static readonly PileRef Waste = new(PileKind.Waste);
	public static PileRef Foundation(int i) => new(PileKind.Foundation, i);
	public static PileRef Tableau(int i) => new(PileKind.Tableau, i);
}

/// <summary>A move of one or more cards from the top of one pile to another.</summary>
public readonly record struct Move(PileRef From, PileRef To, int Count);

/// <summary>A card in a pile, and whether it's face up.</summary>
public readonly record struct Placed(Card Card, bool Up);

/// <summary>
/// Klondike solitaire. Seven tableau piles of one to seven cards, the top card of each face up;
/// the rest is the stock. Turn cards from the stock to the waste, one or three at a time, and
/// go through the stock as often as you like. Build the four foundations up by suit from ace to
/// king. On the tableau, build down in alternating colours; a face-up run can be moved together;
/// only a king (or a run headed by one) may fill an empty pile. A face-down card left on top of a
/// pile turns face up. Win by getting all 52 cards onto the foundations.
/// </summary>
public sealed class Klondike
{
	private readonly List<Placed>[] _tableau = Enumerable.Range(0, 7).Select(_ => new List<Placed>()).ToArray();
	private readonly List<Card>[] _foundations = Enumerable.Range(0, 4).Select(_ => new List<Card>()).ToArray();
	private readonly List<Card> _stock = new();
	private readonly List<Card> _waste = new();
	private readonly Stack<Snapshot> _undo = new();
	private bool _movedThisPass;

	private sealed record Snapshot(List<Placed>[] Tableau, List<Card>[] Foundations, List<Card> Stock, List<Card> Waste, int Moves, int Passes, bool Moved, bool Stuck);

	/// <summary>Deals from a deck (top first), row by row as players do: one card to each pile, then one to piles 2–7, and so on.</summary>
	public Klondike(IReadOnlyList<Card> deck, int drawCount = 1)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("Solitaire needs a full deck.", nameof(deck));
		DrawCount = drawCount;
		int k = 0;
		DealOrder = new List<(Card, int)>();
		for (int row = 0; row < 7; row++)
			for (int pile = row; pile < 7; pile++)
			{
				_tableau[pile].Add(new Placed(deck[k], pile == row));
				DealOrder.Add((deck[k], pile));
				k++;
			}
		// The rest is the stock, the next card to turn on top.
		for (int i = 51; i >= k; i--)
			_stock.Add(deck[i]);
	}

	public int DrawCount { get; }
	public int Moves { get; private set; }
	public int Passes { get; private set; }

	/// <summary>Set when the waste is turned back over without a card having moved since the last time: the stock has nothing new to give.</summary>
	public bool Stuck { get; private set; }

	/// <summary>The cards as dealt to the tableau, in dealing order, with their pile.</summary>
	public List<(Card Card, int Pile)> DealOrder { get; }

	public IReadOnlyList<Placed> Tableau(int pile) => _tableau[pile];
	public IReadOnlyList<Card> Foundation(int i) => _foundations[i];

	/// <summary>The stock, bottom first (the last is the top, the next to turn).</summary>
	public IReadOnlyList<Card> Stock => _stock;

	/// <summary>The waste, bottom first (the last is the top, the one you can play).</summary>
	public IReadOnlyList<Card> Waste => _waste;

	public bool IsWon => _foundations.All(f => f.Count == 13);
	public bool CanUndo => _undo.Count > 0;

	/// <summary>How many steps can be undone (a game can undo back to an earlier count).</summary>
	public int History => _undo.Count;

	/// <summary>Whether every card is face up and the stock is used: the rest plays itself.</summary>
	public bool CanFinish => _stock.Count == 0 && _waste.Count == 0 && _tableau.All(p => p.All(c => c.Up));

	private void Save() => _undo.Push(new Snapshot(
		_tableau.Select(p => p.ToList()).ToArray(), _foundations.Select(f => f.ToList()).ToArray(),
		_stock.ToList(), _waste.ToList(), Moves, Passes, _movedThisPass, Stuck));

	public void Undo()
	{
		if (_undo.Count == 0)
			return;
		var s = _undo.Pop();
		for (int i = 0; i < 7; i++)
		{
			_tableau[i].Clear();
			_tableau[i].AddRange(s.Tableau[i]);
		}
		for (int i = 0; i < 4; i++)
		{
			_foundations[i].Clear();
			_foundations[i].AddRange(s.Foundations[i]);
		}
		_stock.Clear();
		_stock.AddRange(s.Stock);
		_waste.Clear();
		_waste.AddRange(s.Waste);
		Moves = s.Moves;
		Passes = s.Passes;
		_movedThisPass = s.Moved;
		Stuck = s.Stuck;
	}

	/// <summary>Turns the next cards from the stock to the waste, or (if it's empty) turns the waste back over.</summary>
	public bool Draw()
	{
		if (_stock.Count == 0 && _waste.Count == 0)
			return false;
		Save();
		if (_stock.Count == 0)
		{
			for (int i = _waste.Count - 1; i >= 0; i--)
				_stock.Add(_waste[i]);
			_waste.Clear();
			Passes++;
			Stuck = !_movedThisPass && Passes > 1;
			_movedThisPass = false;
		}
		else
		{
			for (int i = 0; i < DrawCount && _stock.Count > 0; i++)
			{
				_waste.Add(_stock[^1]);
				_stock.RemoveAt(_stock.Count - 1);
			}
		}
		Moves++;
		return true;
	}

	/// <summary>The cards a move would carry, bottom first.</summary>
	public List<Card> Cards(Move move) => move.From.Kind switch
	{
		PileKind.Waste => _waste.Skip(_waste.Count - 1).ToList(),
		PileKind.Foundation => _foundations[move.From.Index].Skip(_foundations[move.From.Index].Count - 1).ToList(),
		PileKind.Tableau => _tableau[move.From.Index].Skip(_tableau[move.From.Index].Count - move.Count).Select(p => p.Card).ToList(),
		_ => new List<Card>(),
	};

	public bool IsLegal(Move move)
	{
		if (move.Count < 1 || move.From == move.To || (move.From.Kind == PileKind.Foundation && move.To.Kind == PileKind.Foundation))
			return false;
		// What can leave.
		switch (move.From.Kind)
		{
			case PileKind.Waste:
			case PileKind.Foundation:
				var pile = move.From.Kind == PileKind.Waste ? _waste : _foundations[move.From.Index];
				if (pile.Count == 0 || move.Count != 1)
					return false;
				break;
			case PileKind.Tableau:
				var t = _tableau[move.From.Index];
				if (move.Count > t.Count)
					return false;
				var run = t.Skip(t.Count - move.Count).ToList();
				if (!run.All(p => p.Up))
					return false;
				for (int i = 1; i < run.Count; i++)
					if (run[i].Card.Rank != run[i - 1].Card.Rank - 1 || run[i].Card.IsRed == run[i - 1].Card.IsRed)
						return false;
				break;
			default:
				return false;
		}
		var first = Cards(move)[0];
		// Where it can go.
		switch (move.To.Kind)
		{
			case PileKind.Foundation:
			{
				if (move.Count != 1)
					return false;
				var f = _foundations[move.To.Index];
				return f.Count == 0 ? first.Rank == Card.Ace : f[^1].Suit == first.Suit && first.Rank == f[^1].Rank + 1;
			}
			case PileKind.Tableau:
			{
				var t = _tableau[move.To.Index];
				if (t.Count == 0)
					return first.Rank == Card.King;
				var top = t[^1];
				return top.Up && top.Card.Rank == first.Rank + 1 && top.Card.IsRed != first.IsRed;
			}
			default:
				return false;
		}
	}

	/// <summary>Makes a move; a face-down card left on top of its pile turns face up. Returns that card, if one turned.</summary>
	public Card? Make(Move move)
	{
		if (!IsLegal(move))
			throw new InvalidOperationException("That move isn't allowed.");
		Save();
		var moving = Cards(move);
		switch (move.From.Kind)
		{
			case PileKind.Waste:
				_waste.RemoveAt(_waste.Count - 1);
				break;
			case PileKind.Foundation:
				_foundations[move.From.Index].RemoveAt(_foundations[move.From.Index].Count - 1);
				break;
			case PileKind.Tableau:
				_tableau[move.From.Index].RemoveRange(_tableau[move.From.Index].Count - move.Count, move.Count);
				break;
		}
		if (move.To.Kind == PileKind.Foundation)
			_foundations[move.To.Index].AddRange(moving);
		else
			_tableau[move.To.Index].AddRange(moving.Select(c => new Placed(c, true)));
		Moves++;
		_movedThisPass = true;
		Stuck = false;
		if (move.From.Kind == PileKind.Tableau && _tableau[move.From.Index] is { Count: > 0 } left && !left[^1].Up)
		{
			left[^1] = left[^1] with { Up = true };
			return left[^1].Card;
		}
		return null;
	}

	/// <summary>
	/// The best place to send the card at a position (and the cards on top of it): a foundation
	/// for a single card, otherwise the tableau (preferring a pile that isn't empty). Null if none.
	/// </summary>
	public Move? BestMove(PileRef from, int count)
	{
		if (count == 1 && from.Kind != PileKind.Foundation)
			for (int f = 0; f < 4; f++)
				if (IsLegal(new Move(from, PileRef.Foundation(f), 1)))
					return new Move(from, PileRef.Foundation(f), 1);
		Move? empty = null;
		for (int t = 0; t < 7; t++)
		{
			var move = new Move(from, PileRef.Tableau(t), count);
			if (!IsLegal(move))
				continue;
			if (_tableau[t].Count > 0)
				return move;
			empty ??= move;
		}
		// Don't shuffle a king from one empty-bottomed pile to another.
		if (empty is { } e && from.Kind == PileKind.Tableau && _tableau[from.Index].Count == count)
			return null;
		return empty;
	}

	/// <summary>Every useful move now: to the foundations first, then runs that uncover a card or empty a pile, then the waste to the tableau.</summary>
	public List<Move> UsefulMoves()
	{
		var moves = new List<Move>();
		void Try(Move m)
		{
			if (IsLegal(m))
				moves.Add(m);
		}
		for (int t = 0; t < 7; t++)
			if (_tableau[t].Count > 0)
				for (int f = 0; f < 4; f++)
					Try(new Move(PileRef.Tableau(t), PileRef.Foundation(f), 1));
		if (_waste.Count > 0)
			for (int f = 0; f < 4; f++)
				Try(new Move(PileRef.Waste, PileRef.Foundation(f), 1));
		for (int t = 0; t < 7; t++)
		{
			var pile = _tableau[t];
			int firstUp = pile.FindIndex(p => p.Up);
			if (firstUp < 0)
				continue;
			int count = pile.Count - firstUp;
			// Moving the whole face-up run, except a king from the bottom of one pile to an empty one.
			for (int to = 0; to < 7; to++)
				if (!(firstUp == 0 && _tableau[to].Count == 0))
					Try(new Move(PileRef.Tableau(t), PileRef.Tableau(to), count));
		}
		if (_waste.Count > 0)
			for (int to = 0; to < 7; to++)
				Try(new Move(PileRef.Waste, PileRef.Tableau(to), 1));
		return moves;
	}

	/// <summary>The next move that finishes the game when <see cref="CanFinish"/>: the lowest card that can go up.</summary>
	public Move? FinishingMove()
	{
		Move? best = null;
		int lowest = 99;
		for (int t = 0; t < 7; t++)
		{
			if (_tableau[t].Count == 0)
				continue;
			for (int f = 0; f < 4; f++)
			{
				var m = new Move(PileRef.Tableau(t), PileRef.Foundation(f), 1);
				if (IsLegal(m) && _tableau[t][^1].Card.Rank < lowest)
				{
					best = m;
					lowest = _tableau[t][^1].Card.Rank;
				}
			}
		}
		return best;
	}

	/// <summary>How far a suit's foundation has got (0 if its ace isn't up yet).</summary>
	private int Built(Suit suit) => _foundations.FirstOrDefault(f => f.Count > 0 && f[0].Suit == suit)?.Count ?? 0;

	/// <summary>
	/// A card that can go up to a foundation with no possible harm: an ace or two, or a card whose
	/// opposite-coloured cards one lower are both up already (so nothing will need to go on it).
	/// </summary>
	public Move? SafeMove()
	{
		var sources = new List<PileRef> { PileRef.Waste };
		sources.AddRange(Enumerable.Range(0, 7).Select(PileRef.Tableau));
		foreach (var from in sources)
		{
			Card? top = from.Kind == PileKind.Waste
				? (_waste.Count > 0 ? _waste[^1] : null)
				: (_tableau[from.Index].Count > 0 ? _tableau[from.Index][^1].Card : null);
			if (top is not { } card)
				continue;
			var opposite = card.IsRed ? new[] { Suit.Clubs, Suit.Spades } : new[] { Suit.Diamonds, Suit.Hearts };
			if (card.Rank > 2 && opposite.Min(Built) < card.Rank - 1)
				continue;
			for (int f = 0; f < 4; f++)
				if (IsLegal(new Move(from, PileRef.Foundation(f), 1)))
					return new Move(from, PileRef.Foundation(f), 1);
		}
		return null;
	}

	private int Down(int pile) => _tableau[pile].Count(p => !p.Up);

	/// <summary>
	/// A sensible next move for the computer (the demo, and hints): a card to a foundation, then a
	/// run moved to uncover a face-down card (from the pile with most face down), then the waste to
	/// the tableau, then emptying a pile when a king is waiting for it, then a king into an empty
	/// pile. Null means turn the stock.
	/// </summary>
	public Move? Suggest()
	{
		var moves = UsefulMoves();
		var up = moves.FirstOrDefault(m => m.To.Kind == PileKind.Foundation);
		if (up.Count > 0)
			return up;
		var uncover = moves.Where(m => m.From.Kind == PileKind.Tableau && m.To.Kind == PileKind.Tableau && Down(m.From.Index) > 0)
			.OrderByDescending(m => Down(m.From.Index)).FirstOrDefault();
		if (uncover.Count > 0)
			return uncover;
		var fromWaste = moves.FirstOrDefault(m => m.From.Kind == PileKind.Waste);
		if (fromWaste.Count > 0)
			return fromWaste;
		bool kingWaiting = (_waste.Count > 0 && _waste[^1].Rank == Card.King)
			|| Enumerable.Range(0, 7).Any(t => Down(t) > 0 && _tableau[t][Down(t)].Card.Rank == Card.King);
		if (!kingWaiting)
			return null;
		if (_tableau.Any(p => p.Count == 0))
		{
			int empty = Array.FindIndex(_tableau, p => p.Count == 0);
			if (_waste.Count > 0 && IsLegal(new Move(PileRef.Waste, PileRef.Tableau(empty), 1)))
				return new Move(PileRef.Waste, PileRef.Tableau(empty), 1);
			for (int from = 0; from < 7; from++)
				if (Down(from) > 0 && IsLegal(new Move(PileRef.Tableau(from), PileRef.Tableau(empty), _tableau[from].Count - Down(from))))
					return new Move(PileRef.Tableau(from), PileRef.Tableau(empty), _tableau[from].Count - Down(from));
			return null;
		}
		var emptying = moves.FirstOrDefault(m => m.From.Kind == PileKind.Tableau && m.To.Kind == PileKind.Tableau && Down(m.From.Index) == 0);
		return emptying.Count > 0 ? emptying : null;
	}
}
