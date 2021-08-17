﻿using System;
using System.Threading.Tasks;
using System.Collections.Generic;

using UnityEngine;
using Image = UnityEngine.UI.Image;


using Scuti.GraphQL.Generated;

using LoadedWidgetQueue = System.Collections.Generic.Queue<System.Tuple<Scuti.UI.OfferSummaryPresenter, bool>>;
using GetNextRequestQueue = System.Collections.Generic.Queue<System.Action<Scuti.UI.OfferSummaryPresenter.Model>>;
using System.Threading;
using Scuti.Net;
using UnityEngine.Events;

namespace Scuti.UI
{
    public class OffersPresenter : Presenter<OffersPresenter.Model>
    {
        [Serializable]
        public class Model : Presenter.Model
        {
            public List<OfferSummaryPresenter.Model> Items = new List<OfferSummaryPresenter.Model>();
        }

        [Serializable]
        public class Pagination
        {
            public string Category = string.Empty;
            public int VideoIndex;
            [SerializeField] int _index;
            public int Index
            {
                get { return _index; }
                set { _index = value; }
            }

            public int TotalCount;
        }

        [SerializeField] CategoryNavigator categoryNavigator;

        public bool ShouldUpdateOffers
        {
            get
            {
                return IsOpenOrOpening && (UIManager.Navigator.CurrentModal == null || UIManager.Navigator.CurrentModal == UIManager.TopMenu) && !m_Idle;
            }
        }

        private int _activeVideoOffers = 0;

        [Header("Settings")]
        [SerializeField] int maxOffers = 6;
        [SerializeField] int videoOfferBackFill = 3;
        [SerializeField] int largeOffers = 2;
        [SerializeField] float showDuration = 10;
        [SerializeField] float instantiationInterval = .5f;
        [SerializeField] float showInterval = .5f;
        [SerializeField] bool clearInitialElements = true;

        List<GameObject> initialElements = new List<GameObject>();

        [Header("Instantiation")]
        [SerializeField] OfferSummaryPresenter widgetPrefab_Large;
        [SerializeField] OfferSummaryPresenter widgetPrefab_Small;
        [SerializeField] OfferSummaryRowPresenter widgetPrefab_Double;
        [SerializeField] Transform container_Large;
        [SerializeField] Transform container_Small;
        [SerializeField] Transform container_Video;
        [SerializeField] OfferVideoPresenter videoWidget;

        [Serializable]
        public struct OfferColorData
        {
            public Sprite Background;
            public Color32 Glow;
        }


        public ScutiInfiniteScroll InfinityScroll;

        [Header("Customization")]
        [SerializeField] Image bannerImage;
        [SerializeField] Sprite[] backgrounds;
        [SerializeField] OfferColorData[] colorInfo;

        public BannerWidget Banner;
        public Timer TimeoutTimer;

        public UnityEvent OnPopulateFinished;
        public UnityEvent OnClearFinished;

        private bool m_Idle = false;
        private bool m_Paused = false;
        private bool m_ChangingCategories = false;

        private Vector3 _largeContainerDefaultPosition;

        GetNextRequestQueue GetNextRequestQueue = new GetNextRequestQueue();
        bool m_requestOffersInProgress = false;
        Pagination m_Pagination;
        int m_offerIndex;
        Dictionary<string, Pagination> m_PaginationMap = new Dictionary<string, Pagination>();
        List<OfferSummaryPresenter> m_Instantiated = new List<OfferSummaryPresenter>();


        public void Start()
        {
            if(!clearInitialElements)
            {

                foreach (Transform child in container_Large)
                    if(!initialElements.Contains(child.gameObject)) initialElements.Add(child.gameObject);

                foreach (Transform child in container_Small)
                    if (!initialElements.Contains(child.gameObject)) initialElements.Add(child.gameObject);
            }
        }

        // ================================================
        #region LIFECYCLE
        // ================================================
        public override void Open()
        {
            var first = (firstOpen);// && !(Services.Offer is MockOfferService));
            base.Open();
            if (first)
            {
                UIManager.ShowLoading(true);
                categoryNavigator.OpenCurrent();
            }
            else
            {
                ResumeAds();
            }
            Banner.Open();
        }

        public override void Close()
        {
            base.Close();

            PauseAds();
        }

        private void ResumeAds()
        {
            m_Paused = false;
            m_Idle = false;
            TimeoutTimer.ResetTime(ScutiConstants.SCUTI_TIMEOUT);
            Banner.Play();
            videoWidget?.ResumeTimer();
            TimeoutTimer.Begin();
            foreach (var offer in m_Instantiated)
            {
                if (offer.HasData)
                    offer.ResumeTimer();
            }
        }

        private void PauseAds()
        {
            Banner.Pause();
            videoWidget?.PauseTimer();
            m_Paused = true;
            TimeoutTimer.Pause();
            foreach (var offer in m_Instantiated)
            {
                offer.PauseTimer();
            }
        }

        private void OnTimeout()
        {
            m_Idle = true;
            PauseAds();
        }

        protected override void Awake()
        {
            base.Awake();
            TimeoutTimer.onFinished.AddListener(OnTimeout); 
            if (categoryNavigator)
                categoryNavigator.OnOpenRequest += ShowCategory;
            ProcessLoadedWidgetQueue();
        }

        private void Update()
        {
#if UNITY_IOS || UNITY_ANDROID
            if(Input.touchCount>0)
#else
            if(Input.anyKey || Input.GetAxis("Mouse X") != 0)
#endif
            { 
                ResetTimeout();
            }
            ProcessGetNextRequestQueue();
        }


        // QUEUE HANDLERS
        LoadedWidgetQueue loadedWidgetQueue = new LoadedWidgetQueue();
        async void ProcessLoadedWidgetQueue()
        {
            Debug.LogError("=========== ProcessLoadedWidgetQueue ===========");
            while (true)
            {
                if (!m_Paused && !m_ChangingCategories  && loadedWidgetQueue.Count > 0)
                {
                    var dequeue = loadedWidgetQueue.Dequeue();
                    var widget = dequeue.Item1;
                    var initializing = dequeue.Item2;

                    if (initializing)
                        widget.Show();
                    widget.DisplayCurrentImage();
                    widget.ResetTimer();
                }
                await Task.Delay((int)(showInterval * 1000));
            }
        }

        internal void ResetPagination()
        {
            m_PaginationMap.Clear();
            if(m_Pagination!=null)
                m_Pagination.VideoIndex = 0;
        }

       
#endregion

        // ================================================
#region CATEGORY AND PAGINATION
        // ================================================
        public async void ShowCategory(string category)
        {

            if (TrySetCategory(category))
            { 
                m_ChangingCategories = true;
                Clear();

                if(container_Video!=null && videoWidget!=null) 
                {
                    var offersPage = await ScutiNetClient.Instance.Offer.GetOffers(new List<CampaignType> { CampaignType.Video }, FILTER_TYPE.Eq, m_Pagination.Category, null, null, m_Pagination.VideoIndex, 1);
                    if (offersPage != null && offersPage.Nodes != null && offersPage.Nodes.Count>0)
                    {
                        m_Pagination.VideoIndex++;
                        ShowVideo((offersPage.Nodes as List<Offer>)[0]);
                    } else
                    {
                        m_Pagination.VideoIndex=0;
                        HideVideo();
                    }
                }
                await RequestMoreOffers(true);
            } 
            UIManager.HideLoading(true);
        }

        private int GetActiveLarge()
        {
            return largeOffers + _activeVideoOffers;
        }

        private int GetActiveMax()
        {
            return maxOffers + _activeVideoOffers;
        }

        private void HideVideo()
        {
            _activeVideoOffers = videoOfferBackFill;
            container_Video.gameObject.SetActive(false);
            container_Large.position = container_Video.position;
        }

        private void ShowVideo(Offer offer)
        {
            _activeVideoOffers = 0;
            container_Video.gameObject.SetActive(true);
            container_Large.position = _largeContainerDefaultPosition;
            videoWidget.SetDuration(15f);
            videoWidget.Data = Mappers.GetVideoPresenterModel(offer);
        }

        public bool TrySetCategory(string category)
        {
            if (m_Pagination != null)
            {
                if (m_Pagination.Category != null && m_Pagination.Category.Equals(category))
                {
                    return false;
                }
            }
            UpdatePagination(category);
            return true;
        }

        void UpdatePagination(string categoryName)
        {
            string categoryValue = categoryName;
            if (categoryName.Equals("DEFAULT"))
            {
                categoryValue = null;
            }

            if (!m_PaginationMap.ContainsKey(categoryName))
                m_PaginationMap[categoryName] = new Pagination()
                {
                    Category = categoryValue,
                    Index = 0,
                    VideoIndex = 0
                };

            m_Pagination = m_PaginationMap[categoryName];
        }
#endregion

        // ================================================
#region API
        // ================================================
        /// <summary>
        /// Returns a list of offers, based on the current paginataion status
        /// </summary>
        public async Task RequestMoreOffers(bool replaceData)
        {
            var index = m_Pagination.Index;
            var maxCount = GetActiveMax() * 2;

            //Debug.LogWarning("Requesting Range   index:" + index + "  m_Pagination.Index:" + m_Pagination.Index + "  maxcount:" + maxCount + "  retry:" + retry);
            m_Pagination.Index += maxCount;
            OfferPage offerPage = null;
            try
            {
                offerPage = await ScutiNetClient.Instance.Offer.GetOffers(new List<CampaignType> { CampaignType.Product, CampaignType.Product_Listing }, FILTER_TYPE.In, m_Pagination.Category, null, null, index, maxCount);
            } catch (Exception e)
            {
                ScutiLogger.LogException(e);
                //Debug.LogError("TODO: show error message ");
            }

            if (offerPage != null)
            {
                m_Pagination.TotalCount = offerPage.Paging.TotalCount.GetValueOrDefault(0);
                if (replaceData)
                {
                    Data = Mappers.GetOffersPresenterModel(offerPage.Nodes as List<Offer>);
                }
                else
                {
                    var appendData = Mappers.GetOffersPresenterModel(offerPage.Nodes as List<Offer>);
                    Data.Items.AddRange(appendData.Items);
                }
            }
            else
            {
                if (replaceData)
                {
                    m_Pagination.TotalCount = 0;
                    Data = null;
                }
            }
            //Debug.LogWarning("      actualCount" + actualCount  + "  maxcount:" + maxCount );
            ////if (actualCount < maxCount)
            ////{
            ////    if (m_Pagination.Index > index || resetOverride) m_Pagination.Index = 0; // only reset if it hasn't been already by another offer

            ////    if (actualCount == 0 && retry)
            ////    {
            ////        //Debug.LogError("      *--* m_Pagination.Index:" + m_Pagination.Index);
            ////        return await GetRange(index, maxCount, false);
            ////    }
            ////    else
            ////    {
            ////        // Attempt to wrap back to the start
            ////        index = m_Pagination.Index;
            ////        m_Pagination.Index += maxCount;
            ////        //Debug.LogError("      ** m_Pagination.Index:" + m_Pagination.Index);
            ////        var results = await ScutiNetClient.Instance.Offer.GetOffers(new List<CampaignType> { CampaignType.Product, CampaignType.Product_Listing }, FILTER_TYPE.In, m_Pagination.Category, null, null, index, maxCount - actualCount);
            ////        //Debug.LogError("            ** results -->:" + results.Count);
            ////        if(results!=null && results.Nodes.Count>0)
            ////            offerPage.Nodes.AddRange(results.Nodes);
            ////    }
            ////}
        }

        // Maintains a queue of requests that fetches them one by one. This is 
        // crucial when two offer timers get over pretty much together, the 
        // index for both their next offer requests will be the same and they will
        // get the same offers.
        public Task<OfferSummaryPresenter.Model> GetNext()
        {
            var source = new TaskCompletionSource<OfferSummaryPresenter.Model>();
            GetNextRequestQueue.Enqueue(model => source.SetResult(model));
            return source.Task;
        }

        public void Clear()
        {
            m_offerIndex = 0;
            if (_loadingSource != null)
            {
                _loadingSource.Cancel();
            }
            foreach (var widget in m_Instantiated)
            {
                widget.OnLoaded -= OnWidgetLoaded;
            }
            m_Instantiated.Clear();
            GetNextRequestQueue.Clear();
            loadedWidgetQueue.Clear();
            if (clearInitialElements)
            {
                foreach (Transform child in container_Large)
                    Destroy(child.gameObject);

                foreach (Transform child in container_Small)
                    Destroy(child.gameObject);
            }else
            {
                int children = container_Large.childCount;
                int index = 0;
                for (int i = 0; i < children; ++i)
                {
                    if(initialElements.Contains(container_Large.GetChild(index).gameObject))
                    {
                        index++;
                    }else
                    {
                        Transform child = container_Large.GetChild(index);
                        child.SetParent(null);
                        Destroy(child.gameObject);
                    }
                }

                children = container_Small.childCount;
                index = 0;
                for (int i = 0; i < children; ++i)
                {
                    if (initialElements.Contains(container_Small.GetChild(index).gameObject))
                    {
                        index++;
                    }
                    else
                    {
                        Transform child = container_Large.GetChild(index);
                        child.SetParent(null);
                        Destroy(child.gameObject);
                    }
                }
            }

            Resources.UnloadUnusedAssets();
            OnClearFinished?.Invoke();

        }

        private void ResetTimeout()
        {
            m_Idle = false;
            TimeoutTimer.SoftReset();
        }


#endregion

        // ================================================
#region PRESENTATION
        // ================================================
        CancellationTokenSource _loadingSource;
        override protected void OnSetState()
        {
            Clear();
#pragma warning disable 4014
            _loadingSource = new CancellationTokenSource();
            if (ScutiUtils.IsPortrait())
                PopulatePortraitOffers(_loadingSource.Token);
            else 
                PopulateLandscapeOffers(_loadingSource.Token);
#pragma warning restore 4014
        }

        void ProcessGetNextRequestQueue()
        {
            bool shouldUpdate = ShouldUpdateOffers;
            if (shouldUpdate && m_Paused) ResumeAds();
            else if (!shouldUpdate && !m_Paused) PauseAds();

            if (!m_Paused && GetNextRequestQueue.Count != 0)
            {
                if(m_offerIndex > m_Pagination.Index/2 && m_Pagination.Index<m_Pagination.TotalCount)
                {
                    Debug.Log("Requesting Data:" + m_offerIndex+ " vs " + Data.Items.Count + " index: " + m_Pagination.Index +" and " + m_Pagination.TotalCount);
#pragma warning disable 4014
                    RequestMoreOffers(false);
#pragma warning restore 4014
                }
                if (m_offerIndex < Data.Items.Count)
                {

                    var request = GetNextRequestQueue.Dequeue();
                    Debug.LogError("=========== ProcessGetNextRequestQueue =========== " + m_offerIndex);
                    var model = Data.Items[m_offerIndex];
                    model.Index = m_offerIndex;
                    m_offerIndex++;
                    request?.Invoke(model);
                } else if(m_offerIndex >= m_Pagination.TotalCount)
                {
                    m_offerIndex = 0;
                }
            }
        }

        async private Task PopulatePortraitOffers(CancellationToken cancelToken)
        {
            var max = Math.Min(GetActiveMax(), Data.Items.Count);
            int colorCount = 0;
            int adTakeOverCount = 0;
            int productCount = 0;
            for (int i = 0; i < max; i++)
            {

                var widgetData = Data.Items[i];

                if (widgetData.DisplayAd)
                {
                    adTakeOverCount++;
                } else
                {
                    productCount++;
                }
            }
            m_offerIndex = max;

            // Sometimes in the 2 column ads we have to jump ahead. This stores which ones we have already shown so we can skip    
            HashSet<int> skipIds = new HashSet<int>();

            MonoBehaviour template;
            Transform container = container_Small;
            for (int i = 0; i < max; i++)
            {
                if (skipIds.Contains(i)) continue;

                if (cancelToken.IsCancellationRequested) return;

                var widgetData = Data.Items[i];
                if(widgetData.DisplayAd)
                {
                    template = widgetPrefab_Small;
                    adTakeOverCount--;
                } else
                {
                    productCount--;
                    if(productCount<1)
                    {
                        template = widgetPrefab_Small;
                    } else
                    {
                        template = widgetPrefab_Double;
                    }
                }
               
                var mono = Instantiate(template, container);

                List<OfferSummaryPresenter> offers = new List<OfferSummaryPresenter>();
                List<OfferSummaryPresenter.Model> datas = new List<OfferSummaryPresenter.Model>();
                if (template == widgetPrefab_Double)
                {
                    var multi = mono as OfferSummaryRowPresenter;
                    offers.AddRange(multi.Presenters);
                    datas.Add(widgetData);

                    for(int n = i+1; n<max; n++)
                    {

                        var secondWidget = Data.Items[n];
                        if (secondWidget.DisplayAd) continue;
                        datas.Add(secondWidget);
                        skipIds.Add(n);
                        productCount--;
                        break;
                    }
                }
                else
                {
                    offers.Add(mono as OfferSummaryPresenter);
                    datas.Add(widgetData);
                }

                for (var w = 0; w < offers.Count; w++)
                {
                    var widget = offers[w];
                    widgetData = null;
                    if (w < datas.Count)
                    {
                        widgetData = datas[w];
                    }

                    m_Instantiated.Add(widget);
                    widget.gameObject.hideFlags = HideFlags.DontSave;
                    widget.Inject(GetNext);
                    var colorData = GetColorInfo(colorCount++);
                    widget.SetColorData(colorData.Background, colorData.Glow);
               

                    await Task.Delay((int)(instantiationInterval * 1000));

                    if (cancelToken.IsCancellationRequested) return;

                    widgetData.Index = w+i;
                    widget.Data = widgetData;
                    widget.Data.IsTall = false;
                    widget.Data.LoadImage();
                    widget.OnLoaded += OnWidgetLoaded;

                    widget.OnClick += async () =>
                    {
                        UIManager.ShowLoading(false);
                        var id = widget.Data.ID;
                        var offer = await ScutiNetClient.Instance.Offer.GetOfferByID(id);
                        var panelModel = Mappers.GetOfferDetailsPresenterModel(offer);

                        try
                        {
                            UIManager.OfferDetails.SetData(panelModel);
                            UIManager.OfferDetails.SetIsVideo(!string.IsNullOrEmpty(widget.Data.VideoURL));
                            UIManager.Open(UIManager.OfferDetails);
                        }
                        catch (Exception e)
                        {
                            ScutiLogger.LogException(e);
                            UIManager.Alert.SetHeader("Out of Stock").SetBody("This item is out of stock. Please try again later.").SetButtonText("OK").Show(() => { });
                            //UIManager.Open(UIManager.Offers);
                        }

                        UIManager.HideLoading(false);
                    };
                }
            }


            await Task.Delay(250);
            //Debug.LogWarning(container_Large.childCount+"   ++++++++++++++    "+ container_Small.childCount);
            Debug.LogError("CHECK OUBND");
            //InfinityScroll.CheckBounds();

            OnPopulateFinished?.Invoke();
            m_ChangingCategories = false;
        }

        async private Task PopulateLandscapeOffers(CancellationToken cancelToken)
        {
            for (int i = 0; i < GetActiveMax(); i++)
            {

                if (cancelToken.IsCancellationRequested) return;

                OfferSummaryPresenter template;
                Transform container;

                var index = i;
                
                    // Based on the index, the template and container are chosen.
                    // Currently, the first two offers are large, the other are small
                    template = GetTemplateForIndex(index);
                    container = GetContainerForIndex(index);
                var widget = Instantiate(template, container);
                m_Instantiated.Add(widget);
                widget.gameObject.hideFlags = HideFlags.DontSave;
                widget.Inject(GetNext);
                var colorData = GetColorInfo(index);
                widget.SetColorData(colorData.Background, colorData.Glow);
                widget.SetDuration(showDuration);

                await Task.Delay((int)(instantiationInterval * 1000));

                if (cancelToken.IsCancellationRequested) return;

                // If the index exceeds the count, we don't assign any data to it
                // nor do we listen to the click event. The offer widget does get
                // instantiated but it's just loading and doesn't do anything.
                if (i >= Data.Items.Count)
                {
                    continue;

                }

                widget.Data = Data.Items[index];
                widget.Data.Index = index;
                widget.Data.IsTall = widget.IsTall;
                widget.Data.LoadImage();
                widget.OnLoaded += OnWidgetLoaded;
            

                widget.OnClick += async () =>
                {
                    UIManager.ShowLoading(false);
                    var id = widget.Data.ID;
                    var offer = await ScutiNetClient.Instance.Offer.GetOfferByID(id);
                    var panelModel = Mappers.GetOfferDetailsPresenterModel(offer);

                    try
                    {
                        UIManager.OfferDetails.SetData(panelModel); 
                        UIManager.OfferDetails.SetIsVideo(!string.IsNullOrEmpty(widget.Data.VideoURL));
                        UIManager.Open(UIManager.OfferDetails);
                    } catch(Exception e)
                    {
                        ScutiLogger.LogException(e);
                        UIManager.Alert.SetHeader("Out of Stock").SetBody("This item is out of stock. Please try again later.").SetButtonText("OK").Show(() => { });
                        //UIManager.Open(UIManager.Offers);
                    }

                    UIManager.HideLoading(false);
                };
            }


            await Task.Delay(250);
            //Debug.LogWarning(container_Large.childCount+"   ++++++++++++++    "+ container_Small.childCount);
            OnPopulateFinished?.Invoke();
            m_ChangingCategories = false;
        }

        private void OnWidgetLoaded(bool initial, OfferSummaryPresenter widget)
        {
            loadedWidgetQueue.Enqueue(new Tuple<OfferSummaryPresenter, bool>(widget, initial));
        }

        OfferColorData GetColorInfo(int index)
        {
            if (index >= colorInfo.Length) index = colorInfo.Length - (index % colorInfo.Length)-1;
            return colorInfo[index];
        }

        OfferSummaryPresenter GetTemplateForIndex(int index)
        {
            return index < GetActiveLarge() ? widgetPrefab_Large : widgetPrefab_Small;
        }

        Transform GetContainerForIndex(int index)
        {
            return index < GetActiveLarge() ? container_Large : container_Small;
        }
#endregion
    }
}