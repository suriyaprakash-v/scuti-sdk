﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Scuti.UI
{
    public class UIPanningAndPinchImageLarge : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IScrollHandler
    {
        private Vector3 initialScale;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private float zoomSpeed = 0.1f;
        [SerializeField] private float maxZoomMobile = 10f;
        [SerializeField] private float maxZoomDesktop = 5f;
        [SerializeField] private bool isScale;
        [SerializeField] private bool isEnterToImage;

        private RectTransform rectScroll;
        PointerEventData cachedEventData;
        private int counterTouch;
        private Vector2 lastMousePosition;
        private Vector2 averagePointBetweenTouch;

        [SerializeField] private bool isZoomAutomatic;

        [Header("Testing")]
        [SerializeField]
        bool isTestingMobile;


        // Start is called before the first frame update
        void Awake()
        {
            initialScale = transform.localScale;
            scrollRect = GetComponentInParent<ScrollRect>();
            rectScroll = scrollRect.GetComponent<RectTransform>();

        }

        // ---------------------------------------------------------------------------------------

        public void OnPointerDown(PointerEventData eventData)
        {

        }

        public void OnPointerUp(PointerEventData eventData)
        {

        }

        /// <summary>
        /// IScrollHandler: This method is called when scrolling
        /// </summary>
        /// <param name="eventData"></param>
        public void OnScroll(PointerEventData eventData)
        {
            Debug.Log("OnScroll...");
            //textForDebug.text = "OnScroll";
            var delta = Vector3.one * (eventData.scrollDelta.y * zoomSpeed);
            var desiredScale = transform.localScale + delta;

            desiredScale = ClampDesiredScale(desiredScale);

            transform.localScale = desiredScale;
        }

        // This method anchors the maximum and minimum values of the scale to mobile
        private Vector3 ClampDesiredScale(Vector3 desiredScale)
        {
            desiredScale = Vector3.Max(initialScale, desiredScale);
            desiredScale = Vector3.Min(initialScale * maxZoomMobile, desiredScale);
            return desiredScale;
        }

        void Update()
        {

            if (counterTouch >= 2)
            {
                Touch touchZero = Input.GetTouch(0);
                Touch touchOne = Input.GetTouch(1);

                // Find the position in the previous frame of each touch.
                Vector2 touchZeroPrevPos = touchZero.position - touchZero.deltaPosition;
                Vector2 touchOnePrevPos = touchOne.position - touchOne.deltaPosition;

                averagePointBetweenTouch = (touchZeroPrevPos + touchOnePrevPos) / 2;

                // Find the magnitude of the vector (the distance) between the touches in each frame.
                float prevTouchDeltaMag = (touchZeroPrevPos - touchOnePrevPos).magnitude;
                float touchDeltaMag = (touchZero.position - touchOne.position).magnitude;

                // Find the difference in the distances between each frame.
                float deltaMagnitudeDiff = prevTouchDeltaMag - touchDeltaMag;

                // For scale image
                var delta = Vector3.one * (deltaMagnitudeDiff * -zoomSpeed);
                var desiredScale = transform.localScale + delta;
                desiredScale = ClampDesiredScale(desiredScale);

                transform.localScale = desiredScale;

            }
            #if UNITY_EDITOR || UNITY_STANDALONE_WIN
            if (!isTestingMobile)
            {
                // For desktop version
                if (isEnterToImage)
                {
                    MouseDown(cachedEventData);
                }
            }
            #endif

        }

        // -------------------------------------------------------------------------------------

        /// <summary>
        /// This method will be called on the start of the mouse drag
        /// </summary>
        /// <param name="eventData">mouse pointer event data</param>
        public void OnBeginDrag(PointerEventData eventData)
        {
            Debug.Log("Begin Drag");
            lastMousePosition = eventData.position;
            averagePointBetweenTouch = eventData.position;
        }

        /// <summary>
        /// This method will be called during the mouse/touch drag
        /// </summary>
        /// <param name="eventData">mouse pointer event data</param>
        public void OnDrag(PointerEventData eventData)
        {
            scrollRect.enabled = false;

            Vector2 currentMousePosition = new Vector2(0, 0);

            // Get de position touch
            if (counterTouch == 1)
            {
                currentMousePosition = eventData.position;
            }

            if (counterTouch >= 2)
            {
                currentMousePosition = averagePointBetweenTouch;
            }

            Vector2 diff = currentMousePosition - lastMousePosition;
            RectTransform rect = GetComponent<RectTransform>();

            Vector3 newPosition = rect.position + new Vector3(diff.x, diff.y, 0);
            Vector3 oldPos = rect.position;
            rect.position = newPosition;

            lastMousePosition = currentMousePosition;

        }


        /// <summary>
        /// This method will be called at the end of mouse drag
        /// </summary>
        /// <param name="eventData"></param>
        public void OnEndDrag(PointerEventData eventData)
        {
            scrollRect.enabled = true;
        }

        // ----------------------------------------------------------------------------------------------

        private void MouseDown(PointerEventData dat)
        {
            if (dat == null)
                return;

            Vector2 localCursor;
            var pos1 = dat.position;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectScroll, pos1,
                null, out localCursor))
                return;

            int xpos = (int)(localCursor.x);
            int ypos = (int)(localCursor.y);

            if (xpos < 0) xpos = xpos + (int)rectScroll.rect.width / 2;
            else xpos += (int)rectScroll.rect.width / 2;

            if (ypos > 0) ypos = ypos + (int)rectScroll.rect.height / 2;
            else ypos += (int)rectScroll.rect.height / 2;


            // Calculate point in scale
            float widthScroll = rectScroll.rect.width;
            float heightScroll = rectScroll.rect.height;

            var rect2 = GetComponent<RectTransform>();

            float widthScroll2 = rect2.rect.width;
            float heightScroll2 = rect2.rect.height;

            // Move image in zoom 
            rect2.anchoredPosition = new Vector2(widthScroll2 , heightScroll2 ) - new Vector2(xpos, ypos);
            rect2.anchoredPosition = new Vector2(rect2.anchoredPosition.x * (maxZoomDesktop * 0.95f), rect2.anchoredPosition.y * (maxZoomDesktop * 0.95f));

        }

        // --------------------------------------------------------------------------- Event Trigger

        /// <summary>
        /// Method to detect mouse over enter on image
        /// </summary>
        /// <param name="eventData"></param>
        public void OnPointerEnter(BaseEventData eventData)
        {
            if(isZoomAutomatic)
            {
                if (!isTestingMobile)
                {
                    Debug.Log("UIPinch: Enter mouse to Image ...");
#if UNITY_EDITOR || UNITY_STANDALONE_WIN

                    // Cast BaseEventData as PointerEvenData for tracking mouse
                    PointerEventData pointerData = eventData as PointerEventData;
                    cachedEventData = pointerData;

                    isEnterToImage = true;

                    var delta = Vector3.one * (maxZoomDesktop);
                    var desiredScale = transform.localScale + delta;

                    desiredScale = ClampDesiredScale(desiredScale);

                    transform.localScale = desiredScale;
#endif
                }
            }
        }

        /// <summary>
        /// Method to detect mouse over exit on image
        /// </summary>
        /// <param name="eventData"></param>
        public void OnPointerExit(BaseEventData eventData)
        {
            if (isZoomAutomatic)
            {
                if (!isTestingMobile)
                {                    
#if UNITY_EDITOR || UNITY_STANDALONE_WIN

                    isEnterToImage = false;
                    transform.localScale = initialScale;
#endif
                }
            }
        }

        /// <summary>
        /// Method to detect touch down on the image
        /// </summary>
        /// <param name="eventData"></param>
        public void OnPressDown(BaseEventData eventData)
        {
            counterTouch++;
        }

        /// <summary>
        /// Method to detect touch up on the image
        /// </summary>
        /// <param name="eventData"></param>
        public void OnPressUp(BaseEventData eventData)
        {
            counterTouch--;
        }

    }

}

