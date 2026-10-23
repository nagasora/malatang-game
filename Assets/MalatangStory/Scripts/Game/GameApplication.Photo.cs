namespace MalatangStory.Game
{
    public sealed partial class GameApplication
    {
        private GameScreen photoReturnScreen;
        public void BeginPhoto(bool dish)
        {
            photoReturnScreen = Screen;
            World.PhotoDish = dish && Selected != null;
            World.PhotoYaw = 0; World.PhotoPitch = dish ? 40 : 35; World.PhotoDistance = dish ? 2.2f : 14;
            Open(GameScreen.Photo);
        }
        public void EndPhoto() => Open(photoReturnScreen == GameScreen.Photo ? GameScreen.Store : photoReturnScreen);
    }
}
