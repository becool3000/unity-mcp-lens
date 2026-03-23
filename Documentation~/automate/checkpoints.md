---
uid: checkpoints
---

# Create and restore checkpoints

Use checkpoints to revert your project to an earlier point in time by restoring a previously created snapshot. Assistant creates a checkpoint automatically when you send a prompt, and you can later choose to restore that checkpoint if you want to undo subsequent changes.

## Checkpoints workflow

The checkpoints workflow follows this sequence:

- **Checkpoint created**: When you enable checkpoints and send a prompt, Assistant creates a checkpoint of the current state of your project before making any changes. This checkpoint appears as a flag icon in the Assistant conversation.

   ![Assistant window with the flag icon highlighted](../Images/checkpoint.png)

- **Checkpoint restored** (optional): If you want to revert your project, restore a previously created checkpoint from the conversation. This step is optional and only available if changes were made after that checkpoint.

## Restore a checkpoint

You can restore a checkpoint from the Assistant conversation using the flag icon next to a message. The flag is active only when there are later changes to undo. If there are no subsequent messages (or only messages that have already been reverted), the flag is disabled and restoration isn't available.

When you select the flag to restore to a checkpoint, Unity prompts you to confirm the restore. You can choose not to be prompted again.

When you restore a checkpoint, Unity does the following:

- Reverts all changes made after the checkpoint, whether they were made by Assistant or by you.
- Returns your project to the state it was in when the checkpoint was created.
- Permanently archives later messages in the corresponding Assistant conversation. You can view the messages and actions that were undone, but you can’t return to a point in time later than the restored checkpoint.

A checkpoint can only be restored if there are later changes to undo. If no changes exist after a checkpoint, the restore option is unavailable.

Checkpoints remain available after the Unity Editor restarts and are automatically deleted based on the retention period configured in [**Preferences**](xref:checkpoints-reference).

When you [enable checkpoints](xref:checkpoints-reference) and send a prompt, Assistant creates a checkpoint and automatically saves all open scenes and assets. This ensures checkpoints can reliably restore your project to an earlier state. Restoring a checkpoint overwrites the current project state and might result in loss of unsaved work. In large projects, it might take longer to create checkpoints.

You can enable or disable checkpoints and manage their behavior in **Preferences** > **AI** > **Assistant**. For more information on the available settings, refer to [Checkpoints reference](xref:checkpoints-reference).

## Additional resources

* [Checkpoints reference](xref:checkpoints-reference)
* [Tool and interface reference](xref:tool-reference)