namespace CgfConverter.CryEngineCore;

internal sealed class ChunkSceneProp_744 : ChunkSceneProp
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);

        this.NumProps = b.ReadUInt32();          // Should be 31 for 0x744
        this.PropKey = new string[this.NumProps];
        this.PropValue = new string[this.NumProps];

        // Read the array of scene props and their associated values
        for (int i = 0; i < this.NumProps; i++)
        {
            this.PropKey[i] = b.ReadFString(32);
            this.PropValue[i] = b.ReadFString(64);
        }
    }
}
