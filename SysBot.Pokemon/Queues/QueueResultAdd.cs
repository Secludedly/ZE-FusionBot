namespace SysBot.Pokemon;

public enum QueueResultAdd
{
    /// <summary> Successfully added to the queue. </summary>
    Added,

    /// <summary> Did not add; was already in the queue. </summary>
    AlreadyInQueue,

    /// <summary> Did not add; queue is full. </summary>
    QueueFull,

    /// <summary> Did not add; trade blocked due to non-tradable item (PLZA only). </summary>
    NotAllowedItem,
}
