using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.ButtonHandlers
{
	class InhalerButton
		: MonoBehaviour
	{
		[SerializeField]
		private Button activateInhalerButton;

		public void ActivateInhalerButton()
		{
			activateInhalerButton.interactable = true;
			activateInhalerButton.gameObject.SetActive(true);
		}
	}
}
