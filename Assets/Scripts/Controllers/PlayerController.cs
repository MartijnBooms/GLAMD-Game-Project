using Assets.Scripts.Enumerations;
using Assets.Scripts.Extensions;
using UnityEngine;
using System.Linq;
using Assets.Scripts.Models;
using System;
using Assets.Scripts.Helpers;
using Assets.Scripts.Managers;
using UnityEngine.SceneManagement;
using Assets.Scripts.Utilities;
using Assets.Scripts.ButtonHandlers;

namespace Assets.Scripts.Controllers
{
	public class PlayerController : MonoBehaviour
	{
		private const float GRAVITY = 9.8f;
		private const float INVINCIBLE_TIME_AFTER_DAMAGED = 1.0f;

		[SerializeField]
		private float acceleration;
		[SerializeField]
		private float minSpeed;
		[SerializeField]
		private float maxSpeed;
		[SerializeField]
		private float jumpSpeed;
		[SerializeField]
		private float laneSwapSpeed;
		[Range(0.1f, 1.0f)]
		[SerializeField]
		private float minimumLaneSwapSpeed;

		private float currentSpeed;
		private Lane lane = Lane.Middle;
		private Animator animator;
		private CharacterController characterController;
		private float verticalSpeed = 0.0f;
		private Vector3 targetLanePos;
		private bool reactivateCoinDoubler;
		private bool reactivateSlowmotion;
		private Replay replay = new Replay();

		private void Start()
		{
			GameManager.Instance.Player = this;
			CurrentTile = TileManager.Instance.Tiles.First();
			GetComponentInChildren<SwipeControl>().SetMethodToCall(OnSwipe);

			if (GameManager.Instance.CurrentGame.GameType == GameType.MultiplayerCreate)
			{
				StartCoroutine(CoroutineHelper.Repeat(ReplayGhostController.REPLAY_INTERVAL,
					() =>
					{
						GameManager.Instance.CurrentGame.Replay.Add(new ReplayDataPoint
						{
							Index = replay.Count,
							Orientation = Orientation,
							Position = transform.position
						});
					},
					() => Lives > 0));
			}
		}

		private void Awake()
		{
			characterController = GetComponent<CharacterController>();
			animator = GetComponent<Animator>();
			currentSpeed = (maxSpeed + minSpeed) / 2;
			Orientation = Orientation.North;

			Frozen = true;
			StartCoroutine(CoroutineHelper.Delay(3, () => Frozen = false));
			StartCoroutine(CoroutineHelper.Repeat(
				1.5f,
				() =>
				{
					if (!Frozen)
					{
						Points += 2;
					}
				},
				() => Lives > 0));
		}

		private int _lives = 3;
		public int Lives
		{
			get
			{
				return _lives;
			}
			set
			{
				GameManager.Instance.GuiManager.UpdateLives(_lives = value);
				if (value <= 0)
				{
					GameOver();
				}
				else
				{
					IsInvincible = true;
					StartCoroutine(CoroutineHelper.Delay(INVINCIBLE_TIME_AFTER_DAMAGED, () => IsInvincible = false));
				}
			}
		}

		private int _points = 0;
		public int Points
		{
			get
			{
				return _points;
			}
			set
			{
				GameManager.Instance.GuiManager.UpdatePoints(_points = value);
			}
		}

		private int _coins = 0;
		public int Coins
		{
			get
			{
				return _coins;
			}
			set
			{
				GameManager.Instance.GuiManager.UpdateCoins(_coins = value);
			}
		}

		private int _inhalers = 0;
		public int Inhalers
		{
			get
			{
				return _inhalers;
			}
			set
			{
				GameManager.Instance.GuiManager.UpdateInhalerMeter((_inhalers = value) / (float)PickupController.MAX_NUMBER_OF_INHALERS);
				if (_inhalers == PickupController.MAX_NUMBER_OF_INHALERS)
				{
					GameManager.Instance.GuiManager.ActivateInhalerButton
						.GetComponent<InhalerButton>()
						.ActivateInhalerButton();
				}
			}
		}

		public Orientation Orientation { get; private set; }

		public LanePosition[] TurningPositions { get; set; }

		public bool IsOnLeftCorner { get; set; }

		public bool IsOnRightCorner { get; set; }

		public Tile CurrentTile { get; set; }

		public bool IsDamaged { get; private set; }

		public bool IsCoinDoublerActive { get; private set; }

		public bool InhalerPowerupActive { get; set; }

		public int HorizontalSwipeDirection { get; set; }

		public int VerticalSwipeDirection { get; set; }

		public bool Frozen { get; private set; }

		public bool IsInvincible { get; private set; }

		public bool InhalerUsable
		{
			get
			{
				return Inhalers == PickupController.MAX_NUMBER_OF_INHALERS && !InhalerPowerupActive;
			}
		}

		public bool IsOnCorner
		{
			get
			{
				return (IsOnLeftCorner || IsOnRightCorner) && TurningPositions != null && TurningPositions.Length > 0;
			}
		}

		private void Update()
		{
			// Don't do anything when frozen.
			if (Frozen)
			{
				return;
			}

			// Apply gravity even when damaged.
			ApplyGravity();
			if (IsDamaged)
			{
				return;
			}

			if ((IsOnLeftCorner && Input.GetKeyDown(KeyCode.A)) ||
				(IsOnRightCorner && Input.GetKeyDown(KeyCode.D)))
			{
				TakeCorner();
			}
			else
			{
				CheckForLaneSwap(Swipe.None);
			}

			MoveToCorrectLane();
			Move();
			CheckForJump(Swipe.None);

			//TODO: Don't automatically activate inhaler, only check when button pressed.
			ActivateInhaler(5, 10);
		}

		private void OnSwipe(SwipeControl.SWIPE_DIRECTION direction)
		{
			if (IsDamaged || Frozen)
			{
				return;
			}

			switch (direction)
			{
				case SwipeControl.SWIPE_DIRECTION.SD_UP:
					CheckForJump(Swipe.Up);
					break;
				case SwipeControl.SWIPE_DIRECTION.SD_LEFT:
					if (IsOnLeftCorner){
						TakeCorner();
					}
					else
					{
						CheckForLaneSwap(Swipe.Left);
					}

					break;
				case SwipeControl.SWIPE_DIRECTION.SD_RIGHT:
					if (IsOnRightCorner)
					{
						TakeCorner();
					}
					else
					{
						CheckForLaneSwap(Swipe.Right);
					}

					break;
				default:
					break;
			}
		}

		private void GameOver()
		{
			switch (GameManager.Instance.CurrentGame.GameType)
			{
				case GameType.Singleplayer:
					//TODO: Display appropriate final screen.
					break;
				case GameType.MultiplayerCreate:
					GameManager.Instance.StartCoroutine(ApiManager.MatchCalls.CreateMatch(
						RandomUtilities.Seed,
						GameManager.Instance.CurrentGame.OpponentId.Value,
						GameManager.Instance.User.Id,
						Points + Coins,
						onSuccess: match =>
						{
							GameManager.Instance.StartCoroutine(ApiManager.ReplayCalls.CreateReplay(
								match.Id,
								GameManager.Instance.CurrentGame.Replay.ToString(),
								onSuccess: replayId =>
								{
									//TODO: Display appropriate final screen.
								},
								onFailure: error =>
								{
									//TODO: Handle error.
								}));
						},
						onFailure: error =>
						{
							//TODO: Handle error.
						}));
					break;
				case GameType.MultiplayerChallenge:
					GameManager.Instance.StartCoroutine(ApiManager.MatchCalls.UpdateMatch(
						GameManager.Instance.CurrentGame.Match.Id,
						Points + Coins,
						onSuccess: match =>
						{
							//TODO: Display appropriate final screen.
						},
						onFailure: error =>
						{
							//TODO: Handle error.
						}));
					break;
				default:
					throw new InvalidOperationException("Invalid GameType");
			}

			//TODO: Replace this Coroutine with "Return" button in final screen.
			StartCoroutine(CoroutineHelper.Delay(3.0f, () => SceneManager.LoadScene("MainStartMenu")));
		}

		private void CheckForJump(Swipe swipe)
		{
			if ((swipe == Swipe.Up || Input.GetKeyDown(KeyCode.Space)) && IsAllowedToJump())
			{
				verticalSpeed = jumpSpeed;
			}
		}

		private void ApplyGravity()
		{
			const float gravityMultiplier = 7.5f;

			if (verticalSpeed > 0 || !IsTouchingGround())
			{
				verticalSpeed -= gravityMultiplier * GRAVITY * Time.deltaTime;
				characterController.Move(verticalSpeed * Vector3.up * Time.deltaTime);
			}
		}

		private bool IsAllowedToJump()
		{
			return !IsOnCorner && verticalSpeed <= 0 && IsTouchingGround();
		}

		private bool IsTouchingGround()
		{
			var touching = Physics.Raycast(transform.position + Vector3.up * 0.6f, Vector3.down, 0.2f);
			if (touching)
			{
				verticalSpeed = 0;
			}

			return touching;
		}

		public void TakeFailedCorner()
		{
			if (!IsOnCorner)
			{
				throw new InvalidOperationException("Tried to take invalid corner.");
			}

			IsDamaged = true;
			animator.SetFloat("Speed", 0.0f);
			currentSpeed = minSpeed;

			Lives--;
			if (Lives != ushort.MinValue)
			{
				// Wait until player lands.
				StartCoroutine(CoroutineHelper.WaitUntil(IsTouchingGround, () =>
				{
					animator.Play("Damage");
					StartCoroutine(CoroutineHelper.Delay(0.75f, () => IsDamaged = false));

					TakeCorner();
				}));
			}
		}

		private void TakeCorner()
		{
			// Set position
			var turningPosition = TurningPositions.OrderBy(x => Vector3.Distance(x.Position, transform.position)).First();
			transform.position = Orientation == Orientation.North || Orientation == Orientation.South
				? new Vector3(transform.position.x, transform.position.y, turningPosition.Position.z)
				: new Vector3(turningPosition.Position.x, transform.position.y, transform.position.z);
			lane = turningPosition.Lane;
			targetLanePos = Orientation == Orientation.North || Orientation == Orientation.South
				? Vector3.forward.Multiply(transform.position)
				: Vector3.right.Multiply(transform.position);

			// Set rotation
			Orientation = IsOnLeftCorner ? Orientation.GetLeftOrientation() : Orientation.GetRightOrientation();
			iTween.RotateTo(gameObject, new Vector3(0, (int)Orientation * 90, 0), IsDamaged ? 1.5f : 0.5f);

			// Reset corner variables.
			IsOnLeftCorner = IsOnRightCorner = false;
			TurningPositions = null;
		}

		private void CheckForLaneSwap(Swipe swipe)
		{
			if (IsOnCorner)
			{
				return;
			}

			if (lane != Lane.Left && (swipe == Swipe.Left || Input.GetKeyDown(KeyCode.A)))
			{
				targetLanePos += Orientation.GetLeftOrientation().GetDirectionVector3() * Tile.LANE_DISTANCE;
				lane--;
			}
			else if (lane != Lane.Right && (swipe == Swipe.Right || Input.GetKeyDown(KeyCode.D)))
			{
				targetLanePos += Orientation.GetRightOrientation().GetDirectionVector3() * Tile.LANE_DISTANCE;
				lane++;
			}
		}

		private void MoveToCorrectLane()
		{
			var speed = laneSwapSpeed * Time.deltaTime * Mathf.Max(
				(currentSpeed - minSpeed) / (maxSpeed - minSpeed),
				InhalerPowerupActive ? 1 : minimumLaneSwapSpeed);
			if (Orientation == Orientation.North || Orientation == Orientation.South)
			{
				transform.position = transform.position.CreateNew(x: Mathf.MoveTowards(
					transform.position.x,
					targetLanePos.x,
					speed));
			}
			else
			{
				transform.position = transform.position.CreateNew(z: Mathf.MoveTowards(
					transform.position.z,
					targetLanePos.z,
					speed));
			}
		}

		private void Move()
		{
			var acceleration = this.acceleration;
			if (InhalerPowerupActive)
			{
				acceleration *= 3;
			}
			else if (currentSpeed > maxSpeed)
			{
				acceleration *= 5;
			}

			currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeed, acceleration * Time.deltaTime);
			animator.SetFloat("Speed", currentSpeed);
			transform.position += Orientation.GetDirectionVector3() * currentSpeed * Time.deltaTime;
		}

		private void ActivateInhaler(float duration, float speedBonus)
		{
			const int steps = 100;

			if (InhalerUsable)
			{
				SoundManager.Instance.PlaySound(Sound.InhalerActivate);
				InhalerPowerupActive = true;
				maxSpeed += speedBonus;
				StartCoroutine(CoroutineHelper.For(
					duration / steps,
					() => 0,
					i => i <= steps,
					(ref int i) => i++,
					i => GameManager.Instance.GuiManager.UpdateInhalerMeter(1.0f - 1.0f / steps * i),
					() =>
					{
						Inhalers = 0;
						maxSpeed -= speedBonus;
						InhalerPowerupActive = false;
					}));
			}
		}

		public void ActivateSlowmotion(float duration, float slowmotionFactor)
		{
			const int steps = 100;

			if (Mathf.Approximately(Time.timeScale, slowmotionFactor))
			{
				reactivateSlowmotion = true;
				return;
			}

			Time.timeScale = slowmotionFactor;
			StartCoroutine(CoroutineHelper.For(
				duration / steps,
				() => 0,
				i => i <= steps && Lives > 0,
				(ref int i) =>
				{
					if (reactivateSlowmotion)
					{
						i = 0;
						reactivateSlowmotion = false;
					}
					else
					{
						i++;
					}
				},
				i => GameManager.Instance.GuiManager.UpdateSlowmotionMeter(1.0f - 1.0f / steps * i),
				() => Time.timeScale = 1));
		}

		public void ActivateCoinDoubler(float duration)
		{
			const int steps = 100;

			if (IsCoinDoublerActive)
			{
				reactivateCoinDoubler = true;
				return;
			}

			IsCoinDoublerActive = true;
			StartCoroutine(CoroutineHelper.For(
				duration / steps,
				() => 0,
				i => i <= steps,
				(ref int i) =>
				{
					if (reactivateCoinDoubler)
					{
						i = 0;
						reactivateCoinDoubler = false;
					}
					else
					{
						i++;
					}
				},
				i => GameManager.Instance.GuiManager.UpdateCoinDoublerMeter(1.0f - 1.0f / steps * i),
				() => IsCoinDoublerActive = false));
		}

		public void TakeObstacleDamage()
		{
			IsDamaged = true;
			Lives--;

			if (Lives > 0)
			{
				animator.Play("Damage");
				StartCoroutine(CoroutineHelper.Delay(0.75f, () => IsDamaged = false));
			}

			animator.SetFloat("Speed", 0.0f);
			currentSpeed = minSpeed;
		}
	}
}
