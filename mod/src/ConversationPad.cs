// PZA's WASD & Controller Support (Meitou). Author: PZA. Licence: GPLv3 or later.
// Conversations on the controller (user 2026-11-20, with Dialogue Unframed installed: "how ... can be integrated and guaranteed compatibility").
// - Open while the world publishes a conversation (WorldSnapshot.Conversation, Meitou's own; no other mod is referenced).
// - D-pad up / down move through the replies (wrapping), A says the one chosen through Meitou's own DialogueChoice command, the same command
//   the native window's and Dialogue Unframed's reply buttons end in; the line chosen shows on the game's message line. B still leaves (the
//   window's Esc, PadClicks), the stick still moves the pointer and clicks still work.
// - A new line (other replies) starts the choice at the top again.
// - The views' camera stands aside meanwhile (Driver.OnFrame): Meitou's or Dialogue Unframed's speaker shot keeps the camera.
using Meitou.Game.Host;
using Meitou.Simulation.Dialogue;

namespace PZA.WASDControl.Meitou;

public static class ConversationPad
{
    /// <summary>The reply chosen now (0-based).</summary>
    public static int Index { get; private set; }

    /// <summary>The conversation view the choice belongs to (a new view, a new line: the choice goes back to the top).</summary>
    static ConversationView? s_view;

    /// <summary>The index one <paramref name="step"/> on from <paramref name="index"/> among <paramref name="count"/> replies, wrapping; 0 with
    /// no replies or a broken reading (pure: the offline check and the fuzzer drive it).</summary>
    public static int Move(int index, int count, int step)
    {
        if (count <= 0) return 0;
        long i = ((long)Math.Clamp(index, 0, count - 1) + step) % count;
        return (int)(i < 0 ? i + count : i);
    }

    /// <summary>Whether a conversation is open in <paramref name="session"/>.</summary>
    public static bool Open(WorldSession session) => session.CurrentSnapshot.Conversation is not null;

    /// <summary>The pad's part while a conversation is open: D-pad up / down choose, A says. Returns whether a conversation was open.</summary>
    public static bool Step(WorldSession session, Action<string> notice)
    {
        if (session.CurrentSnapshot.Conversation is not { } talk)
        {
            s_view = null;
            return false;
        }

        int count = talk.Choices.Count;
        if (!ReferenceEquals(talk, s_view) && !SameReplies(talk, s_view)) Index = 0;
        s_view = talk;
        Index = Move(Index, count, 0);

        int step = (Pad.WasPressed(PadButton.DPadDown) ? 1 : 0) - (Pad.WasPressed(PadButton.DPadUp) ? 1 : 0);
        if (step != 0) Choose(talk, step, notice);
        if (Pad.WasPressed(PadButton.South)) Say(session, talk);
        return true;
    }

    /// <summary>Moves the choice <paramref name="step"/> places and shows it (the D-pad; the test hook).</summary>
    internal static void Choose(ConversationView talk, int step, Action<string> notice)
    {
        int count = talk.Choices.Count;
        if (count == 0) return;
        Index = Move(Index, count, step);
        Counters.Add("talk.move");
        notice(Strings.Format("notice.talk_choice", Index + 1, count, talk.Choices[Index]));
    }

    /// <summary>Says the reply chosen through Meitou's DialogueChoice (A; the test hook).</summary>
    internal static void Say(WorldSession session, ConversationView talk)
    {
        if (talk.Choices.Count == 0) return;
        session.World.Commands.Enqueue(new DialogueChoice(Move(Index, talk.Choices.Count, 0)) { Tick = session.World.Tick });
        Counters.Add("talk.say");
    }

    /// <summary>The same replies as before (Meitou may publish a new view object for the same line): the choice stays.</summary>
    static bool SameReplies(ConversationView a, ConversationView? b) =>
        b is not null && a.Text == b.Text && a.Choices.SequenceEqual(b.Choices);
}
