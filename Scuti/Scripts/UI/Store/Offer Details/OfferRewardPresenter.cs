﻿using System;

using UnityEngine;
using UnityEngine.UI;

using Scuti;
using Scuti.GraphQL.Generated;

namespace Scuti.UI {
    public class OfferRewardPresenter : Presenter<OfferRewardPresenter.Model> {
        [Serializable]
        public class Model : Presenter.Model {
            public int scutiReward;
        }

        [SerializeField] Text m_ScutiReward;

        protected override void OnSetState() {
            m_ScutiReward.text = $"+ {Data.scutiReward}";
        }

        internal void SetVariant(ProductVariant productVariant)
        {
            if (productVariant != null)
            {
                OnSetState();
            }
        }
    }
}
