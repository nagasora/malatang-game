using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RestaurantWorld World;
        public RectTransform Thumb;
        private int activePointer=int.MinValue;
        private RectTransform Rect => (RectTransform)transform;
        public void OnPointerDown(PointerEventData e)
        {if(activePointer!=int.MinValue)return;activePointer=e.pointerId;OnDrag(e);}
        public void OnDrag(PointerEventData e)
        {
            if(e.pointerId!=activePointer)return;
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect,e.position,e.pressEventCamera,out var local))
            {
                float radius=Rect.rect.width*.3f;
                var movement=Vector2.ClampMagnitude(local/radius,1);
                Thumb.anchoredPosition=movement*radius;World.TouchMovement=movement;
            }
        }
        public void OnPointerUp(PointerEventData e)
        {if(e.pointerId!=activePointer)return;activePointer=int.MinValue;Thumb.anchoredPosition=Vector2.zero;World.TouchMovement=Vector2.zero;}
        private void OnDisable(){if(World!=null)World.TouchMovement=Vector2.zero;}
        private static Sprite circle;
        public static Sprite Circle()
        {
            if(circle!=null)return circle;
            var texture=new Texture2D(64,64,TextureFormat.RGBA32,false);
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)
                texture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01((31.5f-Vector2.Distance(new Vector2(x,y),new Vector2(31.5f,31.5f)))*2)));
            texture.Apply();circle=Sprite.Create(texture,new Rect(0,0,64,64),new Vector2(.5f,.5f));return circle;
        }
    }
}
