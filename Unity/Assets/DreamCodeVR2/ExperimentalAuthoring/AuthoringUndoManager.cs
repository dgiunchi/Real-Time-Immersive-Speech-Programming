using System;
using System.Collections.Generic;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class AuthoringUndoManager : MonoBehaviour
    {
        [Serializable] private class UndoEntry { public string actionId; public Action undo; }
        public int maximumEntries = 20; private readonly Stack<UndoEntry> entries = new Stack<UndoEntry>();
        public bool CanUndo => entries.Count > 0;
        public void Push(string actionId, Action undo) { if (undo == null) return; while (entries.Count >= Mathf.Max(1, maximumEntries)) { var reversed = entries.ToArray(); entries.Clear(); for (var i=reversed.Length-2;i>=0;i--) entries.Push(reversed[i]); } entries.Push(new UndoEntry { actionId=actionId, undo=undo }); }
        public AuthoringUndoResult UndoLast()
        {
            if (!CanUndo) return new AuthoringUndoResult { success=false, message="No reversible action is available." };
            var entry = entries.Pop(); try { entry.undo(); return new AuthoringUndoResult { actionId=entry.actionId, success=true, message="The last change was undone." }; }
            catch (Exception e) { return new AuthoringUndoResult { actionId=entry.actionId, success=false, message=e.Message }; }
        }
        public void Clear() => entries.Clear();
    }
}
