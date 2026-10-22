using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Renegade.Cards;
using Renegade.Ui;
using Solitaire.Core;

namespace Solitaire.Game;

/// <summary>
/// Klondike, the classic patience. The deck is shuffled in front of you and dealt into seven
/// piles; the stock and waste sit top left and the four foundations top right. Tap a card to
/// send it (and any cards on it) to the best place it can go; tap the stock to turn cards.
/// Undo is free, Hint shows a good move, safe cards can go up by themselves, and once every card
/// is face up the game finishes itself.
/// </summary>
public partial class Main : CardGame
{
	private const float Column = 0.76f, TopZ = -2.7f, TableauZ = -1.5f, Bottom = 3.0f, DownStep = 0.13f, UpStep = 0.31f, FanStep = 0.2f;

	private readonly Dictionary<Card, Card3D> _nodes = new();
	private Klondike? _game;
	private Button _undo = null!;
	private Button _hint = null!;
	private Node3D? _slots;
	private bool _counted;

	/// <summary>The game's undo depth before each of your actions, so Undo takes back an action with the safe cards that followed it.</summary>
	private readonly Stack<int> _actions = new();

	protected override string Title => "Solitaire";
	protected override string Hint => "Tap a card to move it";
	protected override bool HasLevels => false;
	protected override bool HoldsCards => false;
	protected override bool Solitaire => true;
	protected override string NewGameText => "New deal";
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => _game is { IsWon: false, Moves: > 0 };

	private int DrawCount => Settings.Extra.TryGetValue("draw", out var draw) && draw == "3" ? 3 : 1;

	protected override void AddSettings()
	{
		Hud.AddChoice("Draw", new[] { "One card", "Three cards" }, DrawCount == 3 ? 1 : 0, choice =>
		{
			Settings.Extra["draw"] = choice == 1 ? "3" : "1";
			Settings.Save();
			if (!InProgress)
				Run(StartRound());
		});
		Hud.AddChoice("Deals", new[] { "Any shuffle", "Winnable only" }, Flag("winnable") ? 1 : 0, choice => SetFlag("winnable", choice == 1));
		Hud.AddToggle("Safe cards go up by themselves", Flag("autoup", true), on =>
		{
			SetFlag("autoup", on);
			if (on && !Busy)
				Run(AfterMove());
		});

		_undo = Hud.AddButton("Undo", false, Control.LayoutPreset.BottomLeft, 20, 180, -186, -130, 22);
		_undo.Pressed += () =>
		{
			if (!Busy && _game is { CanUndo: true } game)
			{
				int back = _actions.Count > 0 ? _actions.Pop() : game.History - 1;
				while (game.History > back)
					game.Undo();
				Hud.SetNewGameVisible(false);
				Run(Show("Undone.", auto: false));
			}
		};
		_hint = Hud.AddButton("Hint", false, Control.LayoutPreset.BottomRight, -180, -20, -186, -130, 22);
		_hint.Pressed += () =>
		{
			if (!Busy && _game is { IsWon: false })
				Run(ShowHint());
		};
		_undo.Visible = _hint.Visible = false;
	}

	// ------------------------------------------------------------------ Where everything goes

	private static float X(int column) => (column - 3) * Column;
	private static Vector3 StockAt => new(X(0), CardTable.FeltY, TopZ);
	private static Vector3 WasteAt => new(X(1), CardTable.FeltY, TopZ);
	private static Vector3 FoundationAt(int i) => new(X(3 + i), CardTable.FeltY, TopZ);
	private static Vector3 Lifted(Vector3 at, int height) => at + Vector3.Up * (Dealer.CardThickness * (height + 1));

	/// <summary>Where every card belongs now, face up or down.</summary>
	private IEnumerable<(Card Card, Transform3D At)> Places()
	{
		var game = _game!;
		for (int i = 0; i < game.Stock.Count; i++)
			yield return (game.Stock[i], Dealer.InStack(StockAt, i));
		int fanned = DrawCount == 3 ? Mathf.Min(3, game.Waste.Count) : 1;
		for (int i = 0; i < game.Waste.Count; i++)
		{
			int k = i - (game.Waste.Count - fanned);
			var at = WasteAt + new Vector3(Mathf.Max(0, k) * FanStep, 0, 0);
			yield return (game.Waste[i], FaceUpAt(Lifted(at, i)));
		}
		for (int f = 0; f < 4; f++)
			for (int i = 0; i < game.Foundation(f).Count; i++)
				yield return (game.Foundation(f)[i], FaceUpAt(Lifted(FoundationAt(f), i)));
		for (int p = 0; p < 7; p++)
		{
			var pile = game.Tableau(p);
			int down = pile.Count(c => !c.Up), up = pile.Count - down;
			float step = UpStep;
			float room = Bottom - TableauZ - down * DownStep;
			if (up > 1 && (up - 1) * step > room)
				step = room / (up - 1);
			float z = TableauZ;
			for (int i = 0; i < pile.Count; i++)
			{
				var at = Lifted(new Vector3(X(p), CardTable.FeltY, z), i);
				yield return (pile[i].Card, pile[i].Up ? FaceUpAt(at) : FaceDownAt(at));
				z += pile[i].Up ? step : DownStep;
			}
		}
	}

	/// <summary>Moves every card that isn't where it belongs, a little apart. Returns the last flight.</summary>
	private Tween? Layout(float seconds = 0.24f)
	{
		Tween? last = null;
		float delay = 0;
		foreach (var (card, at) in Places())
		{
			var node = _nodes[card];
			if (node.GlobalTransform.IsEqualApprox(at))
				continue;
			bool turning = node.GlobalTransform.Basis.Z.Dot(at.Basis.Z) < 0;
			last = CardMotion.Fly(node, at, seconds, lift: turning ? 0.25f : 0.14f, delay: delay, landed: () => Sounds.PlaySlap(at.Origin));
			delay += 0.03f;
		}
		return last;
	}

	/// <summary>Faint outlines on the felt where piles go, so an empty one still shows.</summary>
	private void BuildSlots()
	{
		if (_slots != null)
			return;
		_slots = new Node3D();
		AddChild(_slots);
		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color(1, 0.95f, 0.85f, 0.1f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		void Slot(Vector3 at, string? mark)
		{
			_slots.AddChild(new MeshInstance3D
			{
				Mesh = new PlaneMesh { Size = new Vector2(Card3D.Width, Card3D.Height) },
				Position = at + Vector3.Up * 0.0006f,
				MaterialOverride = material,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
			if (mark != null)
				_slots.AddChild(new Label3D
				{
					Text = mark,
					FontSize = 96,
					PixelSize = 0.004f,
					Modulate = new Color(1, 0.95f, 0.85f, 0.25f),
					OutlineSize = 0,
					Position = at + Vector3.Up * 0.001f,
					RotationDegrees = new Vector3(-90, 0, 0),
				});
		}
		Slot(StockAt, "↺");
		Slot(WasteAt, null);
		for (int f = 0; f < 4; f++)
			Slot(FoundationAt(f), "A");
		for (int p = 0; p < 7; p++)
			Slot(new Vector3(X(p), CardTable.FeltY, TableauZ), "K");
	}

	// ------------------------------------------------------------------ A deal

	protected override async Task NewRound()
	{
		int round = Round;
		_nodes.Clear();
		_game = null;
		_counted = false;
		_actions.Clear();
		_undo.Visible = _hint.Visible = false;
		BuildSlots();
		SetScore();
		var order = Flag("winnable") ? Autoplay.WinnableDeal(Random, DrawCount) : Deck.Shuffled(Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		var game = new Klondike(order, DrawCount);

		// Square the deck up on the stock's place, then deal row by row, face down.
		Hud.SetStatus("Dealing…", Muted);
		Tween? last = null;
		for (int i = 0; i < nodes.Count; i++)
			last = CardMotion.Slide(nodes[i], Dealer.InStack(StockAt, nodes.Count - 1 - i), 0.3f);
		await Await(last);
		var onPile = new int[7];
		float z0 = TableauZ;
		for (int j = 0; j < game.DealOrder.Count; j++)
		{
			var (card, pile) = game.DealOrder[j];
			int k = onPile[pile]++;
			var at = Lifted(new Vector3(X(pile), CardTable.FeltY, z0 + k * DownStep), k);
			last = Dealer.Deal(_nodes[card], at - Vector3.Up * Dealer.CardThickness, 0, delay: j * 0.045f);
		}
		await Await(last);
		if (round != Round)
			return;
		_game = game;
		await Await(Layout(0.3f));
		if (round != Round)
			return;
		Hud.SetRestartVisible(true);
		_undo.Visible = _hint.Visible = !Demo;
		await AfterMove();
	}

	/// <summary>After anything changes: safe cards up, the auto-finish, a win, or your turn again.</summary>
	private async Task AfterMove(bool auto = true)
	{
		var game = _game!;
		int round = Round;
		Busy = true;
		SetScore();
		while (auto && Flag("autoup", true) && !game.CanFinish && game.SafeMove() is { } safe)
		{
			game.Make(safe);
			await Await(Layout(0.2f));
			if (round != Round)
				return;
		}
		if (game.CanFinish && !game.IsWon)
		{
			Hud.SetStatus("Every card is face up: finishing…", Muted);
			while (game.FinishingMove() is { } move)
			{
				game.Make(move);
				Layout(0.22f);
				await Pause(0.08f);
				if (round != Round)
					return;
			}
			await Pause(0.3f);
		}
		SetScore();
		if (game.IsWon)
		{
			if (!Demo)
			{
				Settings.Extra["won"] = (Count("won") + 1).ToString();
				Settings.Save();
			}
			SetScore();
			_undo.Visible = _hint.Visible = false;
			var tops = Enumerable.Range(0, 4).SelectMany(f => game.Foundation(f).Skip(9)).Select(c => _nodes[c]);
			Finish($"You win, in {game.Moves} moves!", 0, tops, (FoundationAt(0) + FoundationAt(3)) / 2);
			return;
		}
		if (game.Stuck)
		{
			Hud.SetStatus("The stock has nothing new. Undo, or deal again.", Bright);
			Hud.SetNewGameVisible(true);
			if (Demo)
			{
				Finish("No moves left this time.", -1);
				return;
			}
		}
		else
		{
			Hud.SetStatus(game.Stock.Count == 0 && game.Waste.Count > 0 ? "Tap the stock to turn the waste over." : "Your move.", Muted);
		}
		if (Demo)
		{
			await Pause(0.35f);
			if (round == Round)
				await DemoStep();
			return;
		}
		Busy = false;
	}

	private async Task DemoStep()
	{
		var game = _game!;
		if (Autoplay.Next(game) is { } move)
			await Do(move);
		else if (!game.Draw())
			Finish("No moves left this time.", -1);
		else
			await Show(null);
	}

	/// <summary>Makes a move on the table, and shows it.</summary>
	private async Task Do(Move move)
	{
		var game = _game!;
		Busy = true;
		if (!_counted && !Demo)
		{
			_counted = true;
			Settings.Extra["played"] = (Count("played") + 1).ToString();
			Settings.Save();
		}
		_actions.Push(game.History);
		game.Make(move);
		await Show(null);
	}

	/// <summary>Lays the cards out as the game now stands, then carries on.</summary>
	private async Task Show(string? status, bool auto = true)
	{
		int round = Round;
		Busy = true;
		if (status != null)
			Hud.SetStatus(status, Muted);
		Sounds.PlaySwish(Table.Centre);
		await Await(Layout());
		if (round == Round)
			await AfterMove(auto);
	}

	private async Task ShowHint()
	{
		var game = _game!;
		var move = Autoplay.Next(game);
		if (move == null && game.Stuck)
		{
			Hud.SetStatus("No moves left. Undo, or deal again.", Bright);
			return;
		}
		Busy = true;
		var cards = move is { } m ? game.Cards(m) : game.Stock.Count > 0 ? new List<Card> { game.Stock[^1] } : game.Waste.Take(1).ToList();
		Hud.SetStatus(move is { } hint ? $"Try the {Describe(cards[0])}{Where(hint)}." : game.Stock.Count > 0 ? "Turn a card from the stock." : "Turn the waste back over.", Bright);
		var nodes = cards.Select(c => _nodes[c]).ToList();
		Tween? last = null;
		foreach (var node in nodes)
			last = CardMotion.Slide(node, new Transform3D(node.GlobalTransform.Basis, node.GlobalPosition + Vector3.Up * 0.25f), 0.18f);
		await Await(last);
		await Pause(0.45f);
		await Await(Layout(0.18f));
		Busy = false;
	}

	private static string Describe(Card card) => $"{Card.RankName(card.Rank).ToLowerInvariant()} of {card.Suit.ToString().ToLowerInvariant()}";

	private static string Where(Move move) => move.To.Kind switch
	{
		PileKind.Foundation => " up on the foundations",
		_ => "",
	};

	private int Count(string key) => Settings.Extra.TryGetValue(key, out var n) && int.TryParse(n, out int v) ? v : 0;

	private void SetScore()
	{
		string record = $"Won {Count("won")} of {Count("played")}";
		Hud.SetScore(_game == null ? record : $"Moves {_game.Moves}   ·   {record}");
	}

	// ------------------------------------------------------------------ Taps

	/// <summary>Which pile a card is in, and how far up it.</summary>
	private (PileRef Pile, int Index)? Locate(Card card)
	{
		var game = _game!;
		int i = game.Stock.ToList().IndexOf(card);
		if (i >= 0)
			return (PileRef.Stock, i);
		i = game.Waste.ToList().IndexOf(card);
		if (i >= 0)
			return (PileRef.Waste, i);
		for (int f = 0; f < 4; f++)
			if ((i = game.Foundation(f).ToList().IndexOf(card)) >= 0)
				return (PileRef.Foundation(f), i);
		for (int p = 0; p < 7; p++)
			if ((i = game.Tableau(p).Select(c => c.Card).ToList().IndexOf(card)) >= 0)
				return (PileRef.Tableau(p), i);
		return null;
	}

	/// <summary>Where a tap meets the felt.</summary>
	private static Vector3? OnFelt(Vector3 origin, Vector3 direction)
	{
		if (direction.Y > -1e-4f)
			return null;
		float t = (CardTable.FeltY - origin.Y) / direction.Y;
		return origin + direction * t;
	}

	private static bool Near(Vector3 point, Vector3 at) =>
		Mathf.Abs(point.X - at.X) <= Card3D.Width / 2 + 0.04f && Mathf.Abs(point.Z - at.Z) <= Card3D.Height / 2 + 0.04f;

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_game is not { IsWon: false } game)
			return;
		Hud.HideHint();
		var node = HitCard(_nodes.Values, origin, direction);
		var spot = node != null ? Locate(node.Card) : null;
		if (spot == null)
		{
			// The empty stock turns the waste back over.
			if (OnFelt(origin, direction) is { } point && Near(point, StockAt) && game.Waste.Count > 0)
			{
				_actions.Push(game.History);
				game.Draw();
				Run(Show(null));
			}
			return;
		}
		var (pile, index) = spot.Value;
		Move? move = null;
		switch (pile.Kind)
		{
			case PileKind.Stock:
				_actions.Push(game.History);
				if (game.Draw())
					Run(Show(null));
				return;
			case PileKind.Waste:
				move = game.BestMove(pile, 1);
				break;
			case PileKind.Foundation:
				if (index == game.Foundation(pile.Index).Count - 1)
					move = game.BestMove(pile, 1);
				break;
			case PileKind.Tableau:
				if (!game.Tableau(pile.Index)[index].Up)
					return;
				move = game.BestMove(pile, game.Tableau(pile.Index).Count - index);
				break;
		}
		if (move is { } m)
			Run(Do(m));
		else
			Run(Shake(pile.Kind == PileKind.Waste ? _nodes[game.Waste[^1]] : node!));
	}

	/// <summary>A little shake: that card has nowhere to go.</summary>
	private async Task Shake(Card3D node)
	{
		Busy = true;
		Hud.SetStatus($"The {Describe(node.Card)} has nowhere to go.", Muted);
		var home = node.GlobalTransform;
		var tween = node.CreateTween();
		foreach (float dx in new[] { 0.05f, -0.05f, 0.03f, 0f })
			tween.TweenProperty(node, "global_position", home.Origin + new Vector3(dx, 0.02f, 0), 0.05f);
		await Await(tween);
		node.GlobalTransform = home;
		Busy = false;
	}
}
